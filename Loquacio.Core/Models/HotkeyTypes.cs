namespace Loquacio.Models;

/// <summary>
/// Platform-agnostic key identifiers for hotkey bindings.
/// Maps to System.Windows.Input.Key on WPF, KeyCode on Avalonia, ConsoleKey on TUI.
/// </summary>
public enum HotkeyKey
{
    None = 0,
    // Letters
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    // Digits
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    // Function keys
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    // Special
    Space, Enter, Escape, Tab, Back, Delete, Insert,
    Home, End, PageUp, PageDown,
    Left, Right, Up, Down,
    // Numpad
    NumPad0, NumPad1, NumPad2, NumPad3, NumPad4,
    NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    // Punctuation
    OemSemicolon, OemPlus, OemComma, OemMinus, OemPeriod, OemQuestion,
    OemTilde, OemOpenBrackets, OemCloseBrackets, OemQuotes, OemBackslash,
    OemPipe,
}

/// <summary>
/// Platform-agnostic modifier key flags for hotkey bindings.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// Represents a registered global hotkey (platform-agnostic).
/// </summary>
public record HotkeyBinding(HotkeyKey Key, HotkeyModifiers Modifiers, string Action);
