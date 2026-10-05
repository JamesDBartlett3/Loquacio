using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhisperDictation.Core.Services;

namespace WhisperDictation.Daemon.Services;

/// <summary>
/// GitHub-based update service that checks for new releases on GitHub.
/// </summary>
public class GitHubUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubUpdateService> _logger;
    private readonly string _repositoryOwner;
    private readonly string _repositoryName;
    private readonly string _currentVersion;
    private readonly IHostApplicationLifetime? _hostApplicationLifetime;

    public GitHubUpdateService(
        HttpClient httpClient,
        ILogger<GitHubUpdateService> logger,
        IHostApplicationLifetime? hostApplicationLifetime,
        string repositoryOwner,
        string repositoryName,
        string? currentVersion = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _hostApplicationLifetime = hostApplicationLifetime;
        _repositoryOwner = repositoryOwner;
        _repositoryName = repositoryName;
        _currentVersion = currentVersion ?? GetAssemblyVersion();
    }

    /// <inheritdoc/>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{_repositoryOwner}/{_repositoryName}/releases/latest";
            _logger.LogDebug("Checking for updates at {Url}", url);

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: cancellationToken);
            if (release == null)
            {
                _logger.LogWarning("Failed to parse release information");
                return null;
            }

            // Compare versions (simple string comparison for now)
            if (!IsNewerVersion(release.TagName, _currentVersion))
            {
                _logger.LogDebug("No update available. Current: {Current}, Latest: {Latest}", _currentVersion, release.TagName);
                return null;
            }

            _logger.LogInformation("Update available: {LatestVersion} (current: {CurrentVersion})", release.TagName, _currentVersion);

            // Find the appropriate asset for the current platform
            var asset = FindAssetForCurrentPlatform(release.Assets);
            if (asset == null)
            {
                _logger.LogWarning("No update package found for current platform: {Platform}", GetPlatformIdentifier());
                return null;
            }

            // Find SHA256 checksum file for the asset
            var checksumAsset = FindChecksumAsset(release.Assets, asset);
            if (checksumAsset == null)
            {
                _logger.LogError("No SHA256 checksum found for asset {AssetName}. Refusing to install update without verification.", asset.Name);
                return null;
            }

            // Fetch the checksum content
            var checksumResponse = await _httpClient.GetAsync(checksumAsset.BrowserDownloadUrl, cancellationToken);
            checksumResponse.EnsureSuccessStatusCode();
            var checksumContent = await checksumResponse.Content.ReadAsStringAsync(cancellationToken);
            var expectedHash = ParseChecksumFromContent(checksumContent, asset.Name);
            if (string.IsNullOrEmpty(expectedHash))
            {
                _logger.LogError("Failed to parse SHA256 checksum for asset {AssetName} from checksum file.", asset.Name);
                return null;
            }

            return new UpdateInfo
            {
                Version = release.TagName,
                ReleaseNotes = release.Body ?? "No release notes available.",
                DownloadUrl = asset.BrowserDownloadUrl,
                Sha256Hash = expectedHash,
                IsCritical = IsCriticalRelease(release),
                PublishedAt = release.PublishedAt,
                PackageSizeBytes = asset.Size
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking for updates");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(updateInfo.Sha256Hash))
        {
            throw new InvalidOperationException("Cannot download update without SHA256 hash for verification.");
        }
        var tempDir = Path.GetTempPath();
        var fileName = Path.GetFileName(new Uri(updateInfo.DownloadUrl).LocalPath);
        var tempFilePath = Path.Combine(tempDir, $"whisper-dictation-update-{fileName}");

        _logger.LogInformation("Downloading update from {Url} to {Path}", updateInfo.DownloadUrl, tempFilePath);

        try
        {
            var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            var totalRead = 0L;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    var percent = (double)totalRead / totalBytes * 100;
                    progress.Report(percent);
                }
            }

            _logger.LogInformation("Update downloaded successfully: {Path} ({Size} bytes)", tempFilePath, totalRead);

            // Verify SHA256 hash
            var actualHash = await ComputeFileHashAsync(tempFilePath, cancellationToken);
            if (!string.Equals(actualHash, updateInfo.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("SHA256 verification failed. Expected: {Expected}, Actual: {Actual}", updateInfo.Sha256Hash, actualHash);
                File.Delete(tempFilePath);
                throw new InvalidOperationException("Downloaded file failed SHA256 verification. Refusing to install.");
            }

            _logger.LogInformation("SHA256 verification successful: {Hash}", actualHash);
            return tempFilePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update");
            // Clean up partial download
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
            throw;
        }
    }

    /// <inheritdoc/>
    public Task InstallUpdateAsync(string updateFilePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Installing update from {Path}", updateFilePath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return InstallUpdateLinuxAsync(updateFilePath, cancellationToken);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return InstallUpdateWindowsAsync(updateFilePath, cancellationToken);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return InstallUpdateMacOSAsync(updateFilePath, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Unsupported platform for auto-update: {Platform}", RuntimeInformation.OSDescription);
            return Task.CompletedTask;
        }
    }

    private async Task InstallUpdateLinuxAsync(string updateFilePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Linux update installation: showing instructions to user");

        // For Linux, we can't silently install .deb or .AppImage from the daemon
        // Instead, we show a notification with instructions

        var instructions = $@"Update downloaded to: {updateFilePath}

To install:

If .deb package:
  sudo dpkg -i {updateFilePath}

If .AppImage:
  chmod +x {updateFilePath}
  ./updateFilePath

If .tar.gz:
  tar -xzf {updateFilePath} -C /opt/";

        _logger.LogInformation(instructions);

        // In a real implementation, we would:
        // 1. Show a desktop notification
        // 2. Open a file manager to the download location
        // 3. Or use pkexec to install .deb with user confirmation

        await Task.CompletedTask;
    }

    private async Task InstallUpdateWindowsAsync(string updateFilePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Windows update installation: launching installer");

        // For Windows, we can launch the installer with elevated privileges
        // The installer should handle stopping the daemon, updating files, and restarting

        var startInfo = new ProcessStartInfo
        {
            FileName = updateFilePath,
            UseShellExecute = true,
            Verb = "runas", // Request elevation
            Arguments = "/SILENT /NORESTART" // Silent install, no automatic restart
        };

        try
        {
            var process = Process.Start(startInfo);
            if (process != null)
            {
                _logger.LogInformation("Installer launched with PID {Pid}", process.Id);

                // Give the installer time to start, then stop the daemon gracefully
                await Task.Delay(2000, cancellationToken);
                _hostApplicationLifetime?.StopApplication();
            }
            else
            {
                _logger.LogError("Failed to launch installer");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch update installer");
            throw;
        }
    }

    private async Task InstallUpdateMacOSAsync(string updateFilePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("macOS update installation: showing instructions to user");

        // For macOS, we can mount the DMG and copy the .app to /Applications/
        // Or launch the .app directly if it's an updater bundle

        var instructions = $@"Update downloaded to: {updateFilePath}

To install:
  1. Open the downloaded file
  2. Drag WhisperDictation.app to /Applications/
  3. Replace the existing version
  4. Restart Whisper Dictation";

        _logger.LogInformation(instructions);

        // In a real implementation, we would:
        // 1. Use `open` command to launch the DMG/disk image
        // 2. Or use AppleScript to copy .app to /Applications/

        await Task.CompletedTask;
    }

    public static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        // Remove 'v' prefix if present
        var latest = latestVersion.TrimStart('v');
        var current = currentVersion.TrimStart('v');

        // Parse versions for proper numeric comparison
        if (!Version.TryParse(latest, out var latestVer) ||
            !Version.TryParse(current, out var currentVer))
        {
            // Fallback to string comparison if parsing fails
            return string.Compare(latest, current, StringComparison.Ordinal) > 0;
        }

        return latestVer > currentVer;
    }

    private static bool IsCriticalRelease(GitHubRelease release)
    {
        // Check release name or body for critical indicators
        var text = $"{release.Name} {release.Body}".ToLower();
        return text.Contains("critical") || text.Contains("security") || text.Contains("urgent");
    }

    private static GitHubAsset? FindAssetForCurrentPlatform(List<GitHubAsset> assets)
    {
        var platform = GetPlatformIdentifier();

        return assets.FirstOrDefault(a =>
            a.Name.Contains(platform, StringComparison.OrdinalIgnoreCase) &&
            (a.Name.EndsWith(".exe") || a.Name.EndsWith(".zip") ||
             a.Name.EndsWith(".deb") || a.Name.EndsWith(".AppImage") ||
             a.Name.EndsWith(".dmg")));
    }

    private static string GetPlatformIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "osx-x64";
        return "unknown";
    }

    private static string GetAssemblyVersion()
    {
        var assembly = typeof(GitHubUpdateService).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }

    public static GitHubAsset? FindChecksumAsset(List<GitHubAsset> assets, GitHubAsset targetAsset)
    {
        // Look for .sha256 file with same name as target asset
        // GitHub releases typically name checksum files as: <filename>.sha256
        return assets.FirstOrDefault(a =>
            a.Name.Equals($"{targetAsset.Name}.sha256", StringComparison.OrdinalIgnoreCase));
    }

    public static string? ParseChecksumFromContent(string content, string assetName)
    {
        // Expected format: "<hash>  <filename>" or "<hash> <filename>" or just "<hash>"
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
            {
                var hash = parts[0].Trim();
                // Verify it's a 64-character hex string (SHA256)
                if (hash.Length == 64 && hash.All(c => char.IsLetterOrDigit(c)))
                {
                    return hash;
                }
            }
        }
        return null;
    }

    public static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // GitHub API response types
    private record GitHubRelease(
        string TagName,
        string? Name,
        string? Body,
        DateTime PublishedAt,
        List<GitHubAsset> Assets
    );

    public record GitHubAsset(
        string Name,
        string BrowserDownloadUrl,
        long Size
    );
}
