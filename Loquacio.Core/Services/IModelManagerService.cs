namespace Loquacio.Services;

/// <summary>
/// Service for managing Whisper models (download, selection, verification)
/// </summary>
public interface IModelManagerService
{
    /// <summary>
    /// Get the directory where Whisper models are stored
    /// </summary>
    string ModelsDirectory { get; }

    /// <summary>
    /// Get list of available Whisper model types
    /// </summary>
    IReadOnlyList<WhisperModel> AvailableModels { get; }

    /// <summary>
    /// Get list of downloaded models
    /// </summary>
    Task<IReadOnlyList<WhisperModel>> GetDownloadedModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Download a Whisper model
    /// </summary>
    /// <param name="model">Model to download</param>
    /// <param name="progress">Progress callback (0.0 to 1.0)</param>
    /// <param name="ct">Cancellation token</param>
    Task DownloadModelAsync(WhisperModel model, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Delete a downloaded model
    /// </summary>
    Task DeleteModelAsync(WhisperModel model, CancellationToken ct = default);

    /// <summary>
    /// Get the full path for a model
    /// </summary>
    string GetModelPath(WhisperModel model);

    /// <summary>
    /// Check if a model is downloaded. Requires the file to exist with its
    /// exact expected size, so partial downloads do not count as downloaded.
    /// </summary>
    bool IsModelDownloaded(WhisperModel model);

    /// <summary>
    /// Verify a downloaded model's SHA-256 checksum against the expected value
    /// fetched from Hugging Face. Returns false when the file is missing,
    /// corrupted, or the expected checksum cannot be fetched.
    /// </summary>
    Task<bool> ValidateModelChecksumAsync(WhisperModel model, CancellationToken ct = default);
}

/// <summary>
/// Represents a Whisper model type
/// </summary>
public record WhisperModel
{
    /// <summary>
    /// Model name (e.g., "tiny", "base", "small", "medium", "large-v3")
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Model display name
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Exact expected file size in bytes (used to detect partial downloads)
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Recommended use case
    /// </summary>
    public required string UseCase { get; init; }

    /// <summary>
    /// Processing speed relative to tiny model (1x)
    /// </summary>
    public required double SpeedFactor { get; init; }

    /// <summary>
    /// Accuracy relative to tiny model (1x)
    /// </summary>
    public required double AccuracyFactor { get; init; }

    /// <summary>
    /// Ggml model file name
    /// </summary>
    public required string GgmlFileName { get; init; }
}