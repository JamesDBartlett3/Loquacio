using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Loquacio.Avalonia.Services;

/// <summary>
/// Linux desktop platform integration.
/// Manages XDG autostart entries, detects desktop environment, and provides
/// platform-specific paths following XDG Base Directory specification.
/// </summary>
public sealed class LinuxPlatformService : ILinuxPlatformService
{
    private readonly ILogger<LinuxPlatformService> _logger;
    private readonly string _autostartDir;
    private readonly string _autostartFilePath;

    private const string DesktopEntryTemplate = """
        [Desktop Entry]
        Type=Application
        Name={0}
        Comment=Local Whisper-powered voice dictation
        Exec={1}
        Icon={2}
        Terminal=false
        Categories=AudioVideo;Audio;Utility;
        StartupNotify=true
        X-GNOME-Autostart-enabled=true
        """;

    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public string? DesktopEnvironment { get; }

    public string? CurrentDesktop => Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");

    public LinuxPlatformService(ILogger<LinuxPlatformService>? logger = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // XDG_CONFIG_HOME or fallback to ~/.config
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
        {
            configHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        _autostartDir = Path.Combine(configHome, "autostart");
        _autostartFilePath = Path.Combine(_autostartDir, "loquacio.desktop");

        // Detect desktop environment
        DesktopEnvironment = DetectDesktopEnvironment();
    }

    public void EnableAutostart(string executablePath, string displayName = "Loquacio")
    {
        if (!IsLinux)
        {
            _logger.LogDebug("Autostart not applicable on non-Linux platform");
            return;
        }

        try
        {
            Directory.CreateDirectory(_autostartDir);

            // Resolve icon path (look for icon alongside executable, or use generic)
            var iconPath = FindIconPath(executablePath);

            var content = string.Format(DesktopEntryTemplate,
                EscapeDesktopValue(displayName),
                EscapeDesktopValue(executablePath),
                EscapeDesktopValue(iconPath));

            File.WriteAllText(_autostartFilePath, content);

            // Set executable/readable permissions on Linux (UnixFileMode is platform-gated)
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(_autostartFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            _logger.LogInformation("Autostart entry created at {Path}", _autostartFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create autostart entry");
        }
    }

    public void DisableAutostart()
    {
        if (!IsLinux) return;

        try
        {
            if (File.Exists(_autostartFilePath))
            {
                File.Delete(_autostartFilePath);
                _logger.LogInformation("Autostart entry removed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove autostart entry");
        }
    }

    public bool IsAutostartEnabled()
    {
        return File.Exists(_autostartFilePath);
    }

    /// <summary>
    /// Get the XDG data directory for this application (~/.local/share/loquacio/).
    /// </summary>
    public string GetDataDirectory()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");
        }
        return Path.Combine(dataHome, "loquacio");
    }

    /// <summary>
    /// Get the XDG config directory for this application (~/.config/loquacio/).
    /// </summary>
    public string GetConfigDirectory()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
        {
            configHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }
        return Path.Combine(configHome, "loquacio");
    }

    private static string? DetectDesktopEnvironment()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;

        var currentDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        if (!string.IsNullOrEmpty(currentDesktop))
        {
            // XDG_CURRENT_DESKTOP can be colon-separated (e.g., "GNOME:GNOME-Classic")
            return currentDesktop.Split(':')[0];
        }

        // Fall back to environment-specific detection
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GNOME_DESKTOP_SESSION_ID")))
            return "GNOME";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KDE_FULL_SESSION")))
            return "KDE";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MATE_DESKTOP_SESSION_ID")))
            return "MATE";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XFCE_DESKTOP_SESSION_ID")))
            return "XFCE";

        return "Unknown";
    }

    private static string FindIconPath(string executablePath)
    {
        var dir = Path.GetDirectoryName(executablePath);
        if (dir == null) return "audio-input-microphone";

        // Look for icon files alongside the executable
        var candidates = new[]
        {
            Path.Combine(dir, "loquacio.png"),
            Path.Combine(dir, "loquacio.svg"),
            Path.Combine(dir, "..", "share", "icons", "loquacio.png"),
            Path.Combine(dir, "..", "share", "icons", "loquacio.svg"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }

        // Fall back to a standard freedesktop.org icon name
        return "audio-input-microphone";
    }

    /// <summary>
    /// Escape values per the Desktop Entry Specification.
    /// Escape sequences: \s, \n, \t, \r, \\
    /// </summary>
    private static string EscapeDesktopValue(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
