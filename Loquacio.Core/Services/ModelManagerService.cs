using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Loquacio.Services;

/// <summary>
/// Service for managing Whisper models (download, selection, verification)
/// </summary>
public class ModelManagerService : IModelManagerService, IDisposable
{
    private readonly string _modelsDirectory;
    private readonly ILogger<ModelManagerService> _logger;
    private bool _disposed;

    public string ModelsDirectory => _modelsDirectory;

    public IReadOnlyList<WhisperModel> AvailableModels { get; } = new List<WhisperModel>
    {
        new()
        {
            Name = "tiny",
            DisplayName = "Tiny (74MB)",
            SizeBytes = 77_691_713,
            UseCase = "Fastest, lowest accuracy - Good for testing and very low-resource systems",
            SpeedFactor = 1.0,
            AccuracyFactor = 1.0,
            GgmlFileName = "ggml-tiny.bin"
        },
        new()
        {
            Name = "base",
            DisplayName = "Base (141MB)",
            SizeBytes = 147_951_465,
            UseCase = "Good balance of speed and accuracy - Recommended for most use cases",
            SpeedFactor = 1.5,
            AccuracyFactor = 1.3,
            GgmlFileName = "ggml-base.bin"
        },
        new()
        {
            Name = "small",
            DisplayName = "Small (465MB)",
            SizeBytes = 487_601_967,
            UseCase = "High accuracy with reasonable speed - Good for production use",
            SpeedFactor = 2.5,
            AccuracyFactor = 1.6,
            GgmlFileName = "ggml-small.bin"
        },
        new()
        {
            Name = "medium",
            DisplayName = "Medium (1.4GB)",
            SizeBytes = 1_533_763_059,
            UseCase = "Very high accuracy - Slower but best quality for dictation",
            SpeedFactor = 4.5,
            AccuracyFactor = 1.8,
            GgmlFileName = "ggml-medium.bin"
        },
        new()
        {
            Name = "large-v3",
            DisplayName = "Large v3 (2.9GB)",
            SizeBytes = 3_095_033_483,
            UseCase = "Best accuracy - Slower, only for high-end systems",
            SpeedFactor = 8.0,
            AccuracyFactor = 2.0,
            GgmlFileName = "ggml-large-v3.bin"
        }
    };

    // Hugging Face repo hosting the whisper.net ggml models. The expected
    // SHA-256 of each file is fetched from the repo tree API at runtime and
    // is deliberately never hardcoded, so model updates need no code change.
    private const string ModelDownloadBaseUrl = "https://huggingface.co/sandrohanea/whisper.net/resolve/main/classic";
    private const string ModelChecksumApiUrl = "https://huggingface.co/api/models/sandrohanea/whisper.net/tree/main/classic";

    public ModelManagerService(ILogger<ModelManagerService> logger)
        : this(logger, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Loquacio",
            "models"))
    {
    }    public ModelManagerService(ILogger<ModelManagerService> logger, string modelsDirectory)
    {
        _logger = logger;
        _modelsDirectory = modelsDirectory;

        // Ensure models directory exists
        Directory.CreateDirectory(_modelsDirectory);
        _logger.LogInformation("Models directory: {Directory}", _modelsDirectory);
    }

    public async Task<IReadOnlyList<WhisperModel>> GetDownloadedModelsAsync(CancellationToken ct = default)
    {
        var downloaded = new List<WhisperModel>();

        foreach (var model in AvailableModels)
        {
            if (IsModelDownloaded(model))
            {
                downloaded.Add(model);
            }
        }

        return downloaded.AsReadOnly();
    }

    public async Task DownloadModelAsync(WhisperModel model, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsModelDownloaded(model))
        {
            _logger.LogInformation("Model {Model} already downloaded", model.DisplayName);
            return;
        }

        _logger.LogInformation("Starting download of {Model}", model.DisplayName);

        string outputPath = GetModelPath(model);
        string tempOutputPath = outputPath + ".tmp";

        try
        {
            // Delete temp file if exists
            if (File.Exists(tempOutputPath))
            {
                File.Delete(tempOutputPath);
            }

            // Expected SHA-256 comes from the HF repo tree API at runtime
            // (the git-lfs oid of the file). Fail closed: without it we
            // cannot tell a corrupt download from a good one.
            var expectedSha256 = await FetchExpectedSha256Async(model, ct)
                ?? throw new InvalidOperationException(
                    $"Could not fetch the expected SHA-256 checksum for {model.DisplayName} from Hugging Face; " +
                    "refusing to install an unverified download.");

            // Prefer the WhisperGgmlDownloader CLI when present; fall back to a
            // direct HTTP download (works everywhere, reports progress).
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WhisperGgmlDownloader.exe" : "WhisperGgmlDownloader";
            var cliPath = FindOnPath(exeName);

            string actualSha256;
            if (cliPath is not null)
            {
                await DownloadViaCliAsync(cliPath, model, tempOutputPath, ct);
                actualSha256 = await ComputeSha256Async(tempOutputPath, ct);
            }
            else
            {
                actualSha256 = await DownloadViaHttpAsync(model, tempOutputPath, progress, ct);
            }

            // Verify the checksum BEFORE the file is installed — a partial or
            // corrupted download must never be moved into place.
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Checksum mismatch for {model.DisplayName}: expected SHA-256 {expectedSha256}, " +
                    $"got {actualSha256}. The download was corrupt and has been discarded.");
            }

            // Rename temp file to final location
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            File.Move(tempOutputPath, outputPath);

            // Verify file was created
            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException("Model file was not created after download");
            }

            var fileInfo = new FileInfo(outputPath);
            _logger.LogInformation("Model {Model} downloaded successfully (SHA-256 verified): {Size} bytes",
                model.DisplayName, fileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download model {Model}", model.DisplayName);

            // Clean up temp file
            if (File.Exists(tempOutputPath))
            {
                File.Delete(tempOutputPath);
            }

            throw;
        }
    }

    private async Task DownloadViaCliAsync(string cliPath, WhisperModel model, string tempOutputPath, CancellationToken ct)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = $"-m {model.Name} -o \"{tempOutputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();

        // Read stdout and stderr concurrently to avoid deadlock when
        // the process fills one pipe buffer while waiting for the other to be read
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        await stdoutTask;
        var error = await stderrTask;

        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.LogWarning("WhisperGgmlDownloader stderr: {Error}", error);
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"WhisperGgmlDownloader failed with exit code {process.ExitCode}");
        }
    }

    /// <summary>
    /// Downloads the model via direct HTTP, streaming it to <paramref name="tempOutputPath"/>
    /// while computing its SHA-256 on the fly. Returns the checksum of the written file.
    /// </summary>
    protected virtual async Task<string> DownloadViaHttpAsync(
        WhisperModel model, string tempOutputPath, IProgress<double>? progress, CancellationToken ct)
    {
        var url = $"{ModelDownloadBaseUrl}/{model.GgmlFileName}";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Loquacio");

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? model.SizeBytes;
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[1 << 16];
        long read = 0;
        int n;
        while ((n = await httpStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, n), ct);
            sha256.AppendData(buffer, 0, n);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
        progress?.Report(1.0);

        return Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Fetches the expected SHA-256 checksum for the model file from the
    /// Hugging Face repo tree API at runtime (the git-lfs oid of the file).
    /// Returns null when the API is unreachable or the entry cannot be found.
    /// </summary>
    protected virtual async Task<string?> FetchExpectedSha256Async(WhisperModel model, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Loquacio");

        var json = await http.GetStringAsync(ModelChecksumApiUrl, ct);
        using var doc = JsonDocument.Parse(json);
        var expectedPath = $"classic/{model.GgmlFileName}";

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("path", out var path) || path.GetString() != expectedPath)
                continue;
            if (entry.TryGetProperty("lfs", out var lfs) && lfs.TryGetProperty("oid", out var oid))
                return oid.GetString();
        }

        return null;
    }

    /// <summary>
    /// Computes the SHA-256 of a file on disk (streamed, constant memory).
    /// </summary>
    protected virtual async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 16];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct)) > 0)
        {
            sha256.AppendData(buffer, 0, n);
        }
        return Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public Task DeleteModelAsync(WhisperModel model, CancellationToken ct = default)
    {
        string modelPath = GetModelPath(model);

        if (File.Exists(modelPath))
        {
            File.Delete(modelPath);
            _logger.LogInformation("Deleted model {Model} from {Path}", model.DisplayName, modelPath);
        }

        return Task.CompletedTask;
    }

    public string GetModelPath(WhisperModel model)
    {
        return Path.Combine(_modelsDirectory, model.GgmlFileName);
    }

    public bool IsModelDownloaded(WhisperModel model)
    {
        string modelPath = GetModelPath(model);
        if (!File.Exists(modelPath)) return false;

        var length = new FileInfo(modelPath).Length;

        // A partial download almost always has the wrong size; full integrity
        // is enforced by the SHA-256 check at download time (and on demand via
        // ValidateModelChecksumAsync).
        return length > 0 && (model.SizeBytes <= 0 || length == model.SizeBytes);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateModelChecksumAsync(WhisperModel model, CancellationToken ct = default)
    {
        var modelPath = GetModelPath(model);
        if (!File.Exists(modelPath)) return false;

        var expected = await FetchExpectedSha256Async(model, ct);
        if (expected is null) return false;

        var actual = await ComputeSha256Async(modelPath, ct);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Model {Model} failed SHA-256 validation: expected {Expected}, got {Actual}",
                model.DisplayName, expected, actual);
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}