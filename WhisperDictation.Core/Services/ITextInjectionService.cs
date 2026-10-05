namespace WhisperDictation.Services;

/// <summary>
/// Injects text into the currently focused window.
/// Used by the daemon to "type" transcribed text into whatever app has focus.
/// </summary>
public interface ITextInjectionService
{
    /// <summary>
    /// Type text into the focused window, as if the user typed it.
    /// </summary>
    /// <param name="text">The text to inject</param>
    /// <param name="useClipboard">If true, uses clipboard + paste; if false, simulates keystrokes</param>
    /// <returns>True if injection succeeded</returns>
    Task<bool> InjectTextAsync(string text, bool useClipboard = true);
}
