namespace Loquacio.Services;

/// <summary>
/// Defines the activation modes for dictation
/// </summary>
public enum ActivationMode
{
    /// <summary>App listens continuously, processes on silence detection.</summary>
    Continuous,
    /// <summary>App listens only while the push-to-talk hotkey is held.</summary>
    PushToTalk,
    /// <summary>
    /// App listens only after keyword detection, until silence or timeout.
    /// DISABLED: no working keyword recognizer exists yet (the energy-based
    /// spotter could not tell words apart). The daemon refuses to enter this
    /// mode; it stays in the enum for IPC/back-compat until real wake-word
    /// detection (e.g. Porcupine) is implemented.
    /// </summary>
    KeywordActivated
}

/// <summary>
/// Coordinates the activation mode (continuous, push-to-talk, keyword) and
/// orchestrates the audio capture pipeline accordingly.
/// </summary>
public interface IActivationManagerService : IDisposable
{
    ActivationMode CurrentMode { get; }
    bool IsListening { get; }

    event EventHandler<bool>? ListeningStateChanged;
    event EventHandler<ActivationMode>? ModeChanged;

    Task InitializeAsync(CancellationToken ct = default);
    Task SwitchModeAsync(ActivationMode mode, CancellationToken ct = default);
    Task ToggleListeningAsync(CancellationToken ct = default);
    Task CycleModeAsync(CancellationToken ct = default);
    Task StopListeningAsync(CancellationToken ct = default);
    Task OnPushToTalkStartAsync(CancellationToken ct = default);
    Task OnPushToTalkEndAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-apply activation settings (keyword detection) from the settings store.
    /// Called after settings updates via IPC. Hotkeys are re-registered separately
    /// by the daemon's HotkeyManager.
    /// </summary>
    Task ApplyActivationSettingsAsync(CancellationToken ct = default);
    Task ShutdownAsync();
}
