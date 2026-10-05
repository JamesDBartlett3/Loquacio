namespace Loquacio.Services;

/// <summary>
/// Manages auto-start with Windows via the Registry.
/// </summary>
public interface IAutoStartService
{
    /// <summary>
    /// Gets whether auto-start is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Enables auto-start at Windows login.
    /// </summary>
    /// <returns>True if successfully enabled</returns>
    bool Enable();

    /// <summary>
    /// Disables auto-start at Windows login.
    /// </summary>
    /// <returns>True if successfully disabled</returns>
    bool Disable();

    /// <summary>
    /// Toggles auto-start on/off.
    /// </summary>
    /// <returns>The new state</returns>
    bool Toggle();
}
