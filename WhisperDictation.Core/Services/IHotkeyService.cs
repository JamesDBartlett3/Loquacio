using WhisperDictation.Models;

namespace WhisperDictation.Services;

/// <summary>
/// Service for registering and managing global (system-wide) hotkeys
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Event fired when a registered hotkey is pressed
    /// </summary>
    event EventHandler<string>? HotkeyPressed;

    /// <summary>
    /// Register a global hotkey
    /// </summary>
    /// <param name="binding">The key + modifiers + action to register</param>
    /// <returns>True if registration succeeded</returns>
    bool Register(HotkeyBinding binding);

    /// <summary>
    /// Unregister a specific hotkey by action name
    /// </summary>
    void Unregister(string action);

    /// <summary>
    /// Unregister all hotkeys
    /// </summary>
    void UnregisterAll();

    /// <summary>
    /// Check if a hotkey action is currently registered
    /// </summary>
    bool IsRegistered(string action);

    /// <summary>
    /// Get all currently registered hotkeys
    /// </summary>
    IReadOnlyList<HotkeyBinding> GetRegisteredHotkeys();

    /// <summary>
    /// Parse a hotkey string like "Ctrl+Alt+D" into HotkeyKey + HotkeyModifiers
    /// </summary>
    static (HotkeyKey key, HotkeyModifiers modifiers) ParseHotkeyString(string hotkey)
    {
        var modifiers = HotkeyModifiers.None;
        HotkeyKey key = HotkeyKey.None;

        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            if (upper is "CTRL" or "CONTROL")
                modifiers |= HotkeyModifiers.Control;
            else if (upper is "ALT" or "MENU")
                modifiers |= HotkeyModifiers.Alt;
            else if (upper is "SHIFT")
                modifiers |= HotkeyModifiers.Shift;
            else if (upper is "WIN" or "WINDOWS")
                modifiers |= HotkeyModifiers.Windows;
            else if (Enum.TryParse<HotkeyKey>(upper, true, out var parsedKey))
                key = parsedKey;
        }

        return (key, modifiers);
    }

    /// <summary>
    /// Convert a HotkeyKey + HotkeyModifiers back to a display string
    /// </summary>
    static string FormatHotkey(HotkeyKey key, HotkeyModifiers modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(HotkeyModifiers.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Windows))
            parts.Add("Win");

        if (key != HotkeyKey.None)
            parts.Add(key.ToString());

        return string.Join("+", parts);
    }
}
