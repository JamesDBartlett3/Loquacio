namespace WhisperDictation.Daemon.Models;

/// <summary>
/// Hotkey configuration bound from appsettings.json "Hotkeys" section.
/// </summary>
public class DaemonHotkeyOptions
{
    public List<HotkeyEntry> Hotkeys { get; set; } = new();
}

/// <summary>
/// A single configurable hotkey binding for the daemon.
/// </summary>
public class HotkeyEntry
{
    /// <summary>
    /// Key name (e.g. "F1", "Space", "D", "F6").
    /// Must match a known X11 keysym name or Win32 virtual-key name.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Modifier combination as plus-separated string (e.g. "Ctrl+Alt", "Ctrl+Shift").
    /// Valid modifiers: Ctrl, Alt, Shift, Win/Super.
    /// </summary>
    public string Modifiers { get; set; } = string.Empty;

    /// <summary>
    /// Action to perform: "toggle" (start/stop listening), "stop", "mode" (cycle mode).
    /// </summary>
    public string Action { get; set; } = "toggle";
}
