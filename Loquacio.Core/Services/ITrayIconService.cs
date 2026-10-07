namespace Loquacio.Services;

/// <summary>
/// Visual states for the tray icon.
/// </summary>
public enum TrayIconState
{
    /// <summary>Application is idle / not listening.</summary>
    Idle,
    /// <summary>Application is actively listening.</summary>
    Listening,
    /// <summary>Application is processing audio.</summary>
    Processing,
    /// <summary>Application encountered an error.</summary>
    Error
}

/// <summary>
/// Manages the system tray icon, its context menu, and notifications.
/// </summary>
public interface ITrayIconService : IDisposable
{
    void Initialize();
    void Show();
    void Hide();
    void UpdateTooltip(string tooltip);
    void UpdateState(TrayIconState state);
    /// <summary>Updates menu toggle state (mode, LLM post-processing) shown in the tray context menu.</summary>
    void UpdateMenuState(bool isPushToTalk, bool llmEnabled);
    void ShowNotification(string title, string message, int timeout = 0);

    event EventHandler? ShowRequested;
    event EventHandler? ToggleListeningRequested;
    event EventHandler? ToggleModeRequested;
    event EventHandler? ToggleLlmRequested;
    event EventHandler? ExitRequested;
    event EventHandler? SettingsRequested;
}
