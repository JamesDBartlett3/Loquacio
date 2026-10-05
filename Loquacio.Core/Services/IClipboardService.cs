namespace Loquacio.Services;

/// <summary>
/// Service for outputting transcribed text (clipboard, typing simulation)
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Copy text to the system clipboard
    /// </summary>
    /// <param name="text">Text to copy</param>
    Task CopyToClipboardAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Simulate typing text (keystroke injection into the foreground window)
    /// </summary>
    /// <param name="text">Text to type</param>
    /// <param name="delayBetweenKeys">Delay between keystrokes in ms (0 = no delay)</param>
    /// <param name="ct">Cancellation token</param>
    Task TypeTextAsync(string text, int delayBetweenKeys = 0, CancellationToken ct = default);

    /// <summary>
    /// Get or set the output mode ("clipboard" or "type")
    /// </summary>
    string OutputMode { get; set; }
}
