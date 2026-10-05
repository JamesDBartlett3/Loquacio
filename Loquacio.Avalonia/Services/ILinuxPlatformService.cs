namespace Loquacio.Avalonia.Services;

/// <summary>
/// Linux desktop platform integration: autostart, notifications, environment detection.
/// </summary>
public interface ILinuxPlatformService
{
    /// <summary>True if running on Linux.</summary>
    bool IsLinux { get; }

    /// <summary>Detected desktop environment (GNOME, KDE, XFCE, etc.) or null.</summary>
    string? DesktopEnvironment { get; }

    /// <summary>XDG_CURRENT_DESKTOP value.</summary>
    string? CurrentDesktop { get; }

    /// <summary>Enable autostart by creating a .desktop entry in ~/.config/autostart/.</summary>
    /// <param name="executablePath">Path to the Avalonia controller binary.</param>
    /// <param name="displayName">Display name for the autostart entry.</param>
    void EnableAutostart(string executablePath, string displayName = "Loquacio");

    /// <summary>Remove the autostart .desktop entry.</summary>
    void DisableAutostart();

    /// <summary>True if the autostart entry currently exists.</summary>
    bool IsAutostartEnabled();
}

/// <summary>
/// Stub for non-Linux platforms. All operations are no-ops.
/// </summary>
public sealed class NullLinuxPlatformService : ILinuxPlatformService
{
    public bool IsLinux => false;
    public string? DesktopEnvironment => null;
    public string? CurrentDesktop => null;
    public void EnableAutostart(string executablePath, string displayName = "Loquacio") { }
    public void DisableAutostart() { }
    public bool IsAutostartEnabled() => false;
}
