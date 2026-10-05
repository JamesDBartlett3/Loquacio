using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WhisperDictation.Services;

/// <summary>
/// LLM post-processor that sends Whisper transcription output to a local LLM
/// (LM Studio or Ollama) via the OpenAI-compatible API for correction.
/// </summary>
public class LLMPostProcessorService : ILLMPostProcessorService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<LLMPostProcessorService> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    private const string LmStudioDefaultUrl = "http://localhost:1234/v1";
    private const string OllamaDefaultUrl = "http://localhost:11434/v1";

    // Common filler words to remove
    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "um", "uh", "ehm", "erm", "ah", "er", "hmm", "mm", "mhm", "like", "you know", "sort of", "kind of"
    };

    // Default system prompt for LLM correction
    private const string SystemPrompt = """
        You are a transcription correction assistant. Your job is to clean up speech-to-text output.
        Rules:
        1. Fix obvious misheard words and spelling errors
        2. Add proper punctuation (commas, periods, question marks)
        3. Remove filler words (um, uh, ehm, etc.)
        4. Do NOT change the meaning or add information
        5. Do NOT rephrase or reword — only correct
        6. Preserve the original speaker's voice and intent
        7. If the text already looks correct, return it unchanged
        Return ONLY the corrected text, no explanations.
        """;

    public bool IsAvailable { get; private set; }
    public bool IsEnabled { get; private set; }

    public event EventHandler<PostProcessingEventArgs>? OnPostProcessingCompleted;

    public LLMPostProcessorService(
        ISettingsService settingsService,
        ILogger<LLMPostProcessorService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<string?> AutoDetectProviderAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Auto-detecting LLM provider");

        // Try LM Studio first
        if (await ProbeEndpointAsync(LmStudioDefaultUrl + "/models", ct))
        {
            _logger.LogInformation("Auto-detected LM Studio at {Url}", LmStudioDefaultUrl);
            return "lm-studio";
        }

        // Try Ollama
        if (await ProbeEndpointAsync(OllamaDefaultUrl + "/models", ct))
        {
            _logger.LogInformation("Auto-detected Ollama at {Url}", OllamaDefaultUrl);
            return "ollama";
        }

        _logger.LogWarning("No LLM provider detected");
        return null;
    }

    public async Task<string> ProcessAsync(string input, IEnumerable<string>? vocabulary = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        // Step 1: Always remove filler words (works without LLM)
        var cleaned = RemoveFillerWords(input);

        // Step 2: If LLM is not enabled or not available, return cleaned text
        if (!IsEnabled || !IsAvailable)
        {
            _logger.LogDebug("LLM post-processing skipped (enabled={Enabled}, available={Available})", IsEnabled, IsAvailable);
            return cleaned;
        }

        // Step 3: Send to LLM for correction
        await _processingLock.WaitAsync(ct);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = await CallLLMAsync(cleaned, vocabulary, ct);
            sw.Stop();

            var args = new PostProcessingEventArgs
            {
                OriginalText = input,
                ProcessedText = result,
                WasProcessed = true,
                ProcessingTime = sw.Elapsed
            };
            OnPostProcessingCompleted?.Invoke(this, args);

            _logger.LogInformation("LLM post-processing completed in {Ms}ms", sw.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("LLM post-processing cancelled");
            return cleaned;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM post-processing failed, returning cleaned text");
            return cleaned;
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        var endpoint = settings.LLM.Endpoint;

        try
        {
            var url = endpoint.TrimEnd('/') + "/models";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _httpClient.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(content);
                var modelCount = doc.RootElement.TryGetProperty("models", out var models)
                    ? models.GetArrayLength()
                    : doc.RootElement.TryGetProperty("data", out var data) ? data.GetArrayLength() : 0;
                IsAvailable = true;
                return (true, $"✓ Connected — {modelCount} model(s) available");
            }

            return (false, $"✗ HTTP {resp.StatusCode}: {resp.ReasonPhrase}");
        }
        catch (TaskCanceledException)
        {
            return (false, "✗ Connection timed out (5s)");
        }
        catch (Exception ex)
        {
            return (false, $"✗ {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<LlmModelInfo>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        var endpoint = settings.LLM.Endpoint.TrimEnd('/');
        var provider = settings.LLM.Provider;

        // The OpenAI-compatible /v1/models responses return bare ids only; the
        // provider-native endpoints carry the metadata shown in model tooltips.
        var lmStudioUrl = endpoint.Replace("/v1", "/api/v1", StringComparison.OrdinalIgnoreCase) + "/models";
        var ollamaUrl = endpoint.Replace("/v1", "/api", StringComparison.OrdinalIgnoreCase) + "/tags";
        var openAiUrl = endpoint + "/models";

        if (string.Equals(provider, "lm-studio", StringComparison.OrdinalIgnoreCase))
            return await TryFetchModelsAsync(new[] { lmStudioUrl }, ct);

        if (string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase))
            return await TryFetchModelsAsync(new[] { ollamaUrl, openAiUrl }, ct);

        return await TryFetchModelsAsync(new[] { lmStudioUrl, ollamaUrl, openAiUrl }, ct);
    }

    private async Task<IReadOnlyList<LlmModelInfo>> TryFetchModelsAsync(string[] urls, CancellationToken ct)
    {
        foreach (var url in urls)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = document.RootElement;
                var arrayName = root.TryGetProperty("models", out _) ? "models" : "data";
                if (!root.TryGetProperty(arrayName, out var data) || data.ValueKind != JsonValueKind.Array)
                    continue;

                var models = data.EnumerateArray()
                    .Where(model => model.TryGetProperty("key", out _) || model.TryGetProperty("id", out _) || model.TryGetProperty("name", out _))
                    .Select(model => model.TryGetProperty("details", out _) ? ParseOllamaModel(model) : ParseModel(model))
                    .ToList();
                if (models.Count > 0)
                    return models;
            }
            catch (Exception) when (urls.Length > 1)
            {
                // Try the next candidate URL (e.g. non-LM-Studio server or provider offline).
            }
        }

        return [];
    }

    private static LlmModelInfo ParseOllamaModel(JsonElement model)
    {
        var details = model.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
        return new LlmModelInfo
        {
            Id = GetString(model, "name", "model", "id"),
            Architecture = GetAnyString(details, "family"),
            Parameters = GetAnyString(details, "parameter_size"),
            Quantization = GetAnyString(details, "quantization_level"),
            MemorySize = FormatBytes(GetAnyString(model, "size")),
        };
    }

    private static LlmModelInfo ParseModel(JsonElement model)
    {
        var metadata = model.TryGetProperty("metadata", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : model;
        var quantization = metadata.TryGetProperty("quantization", out var quant) ? quant : default;
        var quantizationName = quantization.ValueKind switch
        {
            JsonValueKind.Object => GetAnyString(quantization, "name"),
            // LM Studio reports null for unquantized models
            JsonValueKind.Null => "None (full precision)",
            _ => GetAnyString(metadata, "quantization", "quantization_level", "quant"),
        };
        var memorySize = GetAnyString(metadata, "size_bytes", "memory_size", "size", "file_size");
        return new LlmModelInfo
        {
            Id = GetString(model, "key", "id"),
            Architecture = GetAnyString(metadata, "architecture", "arch", "model_architecture"),
            Parameters = GetAnyString(metadata, "params_string", "parameters", "parameter_count", "params"),
            Publisher = GetAnyString(metadata, "publisher", "owned_by", "organization"),
            Quantization = quantizationName,
            MemorySize = FormatBytes(memorySize)
        };
    }

    private static string GetAnyString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return "Unknown";
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "Unknown";
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
        }
        return "Unknown";
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string FormatBytes(string value)
    {
        if (!long.TryParse(value, out var bytes) || bytes < 0) return value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    /// <summary>
    /// Update availability state from settings
    /// </summary>
    public async Task RefreshStateAsync(CancellationToken ct = default)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);

        // LLM is enabled if provider is not "none" and Enabled flag is true
        IsEnabled = settings.LLM.Enabled &&
                   !string.Equals(settings.LLM.Provider, "none", StringComparison.OrdinalIgnoreCase);

        // Auto-detect if configured
        if (string.Equals(settings.LLM.Provider, "auto-detect", StringComparison.OrdinalIgnoreCase))
        {
            var detected = await AutoDetectProviderAsync(ct);
            IsAvailable = detected != null;
        }
        else
        {
            var (success, _) = await TestConnectionAsync(ct);
            IsAvailable = success;
        }

        _logger.LogInformation("LLM post-processor state: enabled={Enabled}, available={Available}", IsEnabled, IsAvailable);
    }

    private async Task<string> CallLLMAsync(string text, IEnumerable<string>? vocabulary, CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        var endpoint = settings.LLM.Endpoint.TrimEnd('/');
        var model = settings.LLM.Model;

        // Build the user prompt
        var userPrompt = BuildUserPrompt(text, vocabulary);

        // If model is "auto", try to get the first available model
        if (string.Equals(model, "auto", StringComparison.OrdinalIgnoreCase))
        {
            model = await GetFirstModelAsync(endpoint, ct) ?? "default";
        }

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1,  // Low temperature for consistent corrections
            max_completion_tokens = Math.Max(text.Length * 2, 500),  // Enough room for corrections
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint + "/chat/completions") { Content = content };
        using var resp = await _httpClient.SendAsync(req, ct);

        resp.EnsureSuccessStatusCode();

        var responseJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var resultText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return resultText?.Trim() ?? text;
    }

    private static string BuildUserPrompt(string text, IEnumerable<string>? vocabulary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Correct the following transcribed speech:");

        if (vocabulary != null)
        {
            var vocabList = vocabulary.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
            if (vocabList.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Custom vocabulary (these words are likely correct, use them when appropriate):");
                sb.AppendLine(string.Join(", ", vocabList));
            }
        }

        sb.AppendLine();
        sb.AppendLine("Transcribed text:");
        sb.AppendLine(text);

        return sb.ToString();
    }

    private async Task<string?> GetFirstModelAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint + "/models");
            using var resp = await _httpClient.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                return data[0].GetProperty("id").GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-detect model from {Endpoint}", endpoint);
        }

        return null;
    }

    private async Task<bool> ProbeEndpointAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _httpClient.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Remove common filler words from text (works without LLM)
    /// </summary>
    public static string RemoveFillerWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var result = text;

        // Remove filler words as standalone tokens
        foreach (var filler in FillerWords)
        {
            // Match word boundaries for single words
            if (filler.Contains(' '))
            {
                // Multi-word phrases — simple replace (case-insensitive)
                result = ReplaceCaseInsensitive(result, filler, string.Empty);
            }
            else
            {
                // Single word — use regex-style boundary matching
                result = RemoveWord(result, filler);
            }
        }

        // Clean up double spaces and fix capitalization after removal
        result = CleanWhitespace(result);

        return result.Trim();
    }

    private static string RemoveWord(string text, string word)
    {
        // Simple word-boundary removal without regex dependency
        var sb = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            // Check if we're at a word boundary
            if (IsWordBoundary(text, i) &&
                i + word.Length <= text.Length &&
                string.Equals(text.Substring(i, word.Length), word, StringComparison.OrdinalIgnoreCase) &&
                (i + word.Length == text.Length || !char.IsLetterOrDigit(text[i + word.Length])))
            {
                // Skip the filler word
                i += word.Length;
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    private static bool IsWordBoundary(string text, int pos)
    {
        if (pos == 0) return true;
        return !char.IsLetterOrDigit(text[pos - 1]);
    }

    private static string ReplaceCaseInsensitive(string text, string search, string replacement)
    {
        var sb = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            if (i + search.Length <= text.Length &&
                string.Equals(text.Substring(i, search.Length), search, StringComparison.OrdinalIgnoreCase))
            {
                // Check boundaries
                var beforeOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                var afterOk = i + search.Length >= text.Length || !char.IsLetterOrDigit(text[i + search.Length]);

                if (beforeOk && afterOk)
                {
                    sb.Append(replacement);
                    i += search.Length;
                    continue;
                }
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    private static string CleanWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Replace multiple spaces with single
        while (text.Contains("  "))
        {
            text = text.Replace("  ", " ");
        }

        // Fix space before punctuation
        text = text.Replace(" ,", ",").Replace(" .", ".").Replace(" ;", ";").Replace(" :", ":");
        text = text.Replace(" ?", "?").Replace(" !", "!");

        // Fix orphaned punctuation left after filler word removal
        // e.g., "Hello, , world" → "Hello, world"
        while (text.Contains(", ,")) text = text.Replace(", ,", ",");
        while (text.Contains(",,")) text = text.Replace(",,", ",");
        // Trim leading orphaned punctuation (from filler removal at string start)
        text = text.TrimStart(',', ' ', ';', ':');

        // Capitalize first letter after period (sentence boundary)
        // NOTE: Removed — RemoveFillerWords should preserve original capitalization.
        // Capitalization is a post-processing concern, not a filler-removal concern.

        return text;
    }
}
