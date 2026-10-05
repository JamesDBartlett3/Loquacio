namespace WhisperDictation.Core.Services;

/// <summary>
/// Service for checking for and applying application updates.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Check for available updates asynchronously.
    /// </summary>
    /// <returns>Update information if an update is available, null otherwise.</returns>
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Download an update to a temporary location.
    /// </summary>
    /// <param name="updateInfo">The update information.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the downloaded update file.</returns>
    Task<string> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Install the downloaded update.
    /// </summary>
    /// <param name="updateFilePath">Path to the downloaded update file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// This method will typically exit the application to complete the update.
    /// The exact behavior depends on the platform and update mechanism.
    /// </remarks>
    Task InstallUpdateAsync(string updateFilePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about an available update.
/// </summary>
public record UpdateInfo
{
    /// <summary>
    /// The version of the update.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The release notes for this update.
    /// </summary>
    public required string ReleaseNotes { get; init; }

    /// <summary>
    /// The URL to download the update package.
    /// </summary>
    public required string DownloadUrl { get; init; }

    /// <summary>
    /// The SHA256 hash of the update package for verification.
    /// </summary>
    public required string? Sha256Hash { get; init; }

    /// <summary>
    /// Whether this is a critical update that should be installed immediately.
    /// </summary>
    public bool IsCritical { get; init; }

    /// <summary>
    /// The publication date of the release.
    /// </summary>
    public DateTime PublishedAt { get; init; }

    /// <summary>
    /// The size of the update package in bytes.
    /// </summary>
    public long PackageSizeBytes { get; init; }
}
