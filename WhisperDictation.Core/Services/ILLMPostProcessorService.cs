using WhisperDictation.Models;

namespace WhisperDictation.Services;

/// <summary>
/// Service for post-processing transcription text through a local LLM
/// for auto-correction, punctuation, and filler word removal.
/// </summary>
public interface ILLMPostProcessorService
{
    /// <summary>
    /// Whether an LLM endpoint is currently available
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether post-processing is enabled in settings
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Detect available LLM endpoints (LM Studio, Ollama)
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Detected provider name, or null if none found</returns>
    Task<string?> AutoDetectProviderAsync(CancellationToken ct = default);

    /// <summary>
    /// Post-process transcription text through LLM
    /// </summary>
    /// <param name="input">Raw Whisper transcription</param>
    /// <param name="vocabulary">Custom vocabulary words to include as context</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Corrected text, or original text if LLM unavailable</returns>
    Task<string> ProcessAsync(string input, IEnumerable<string>? vocabulary = null, CancellationToken ct = default);

    /// <summary>
    /// Test connection to the configured LLM endpoint
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tuple of (success, message)</returns>
    Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Get available models from the configured LLM endpoint
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of available models, or empty list if unavailable</returns>
    Task<IReadOnlyList<LlmModelInfo>> GetAvailableModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Event fired when post-processing completes
    /// </summary>
    event EventHandler<PostProcessingEventArgs>? OnPostProcessingCompleted;
}

/// <summary>
/// Event args for post-processing completion
/// </summary>
public class PostProcessingEventArgs : EventArgs
{
    public string OriginalText { get; init; } = string.Empty;
    public string ProcessedText { get; init; } = string.Empty;
    public bool WasProcessed { get; init; }
    public TimeSpan ProcessingTime { get; init; }
}
