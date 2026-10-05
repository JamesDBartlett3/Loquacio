namespace WhisperDictation.Avalonia.ViewModels;

/// <summary>
/// General settings tab — activation mode, output mode, hotkey display.
/// In controller mode, these send IPC commands to the daemon rather than
/// directly modifying services.
/// </summary>
public partial class GeneralSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _activationMode = "continuous";

    [ObservableProperty]
    private string _hotkeyDisplay = "Ctrl+Alt+D";

    [ObservableProperty]
    private bool _keywordEnabled = true;

    [ObservableProperty]
    private string _keyword = "Hey Dictate";

    [ObservableProperty]
    private string _outputMode = "clipboard";

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _isRecordingHotkey;

    public string[] Modes { get; } = { "continuous", "push-to-talk", "keyword" };
    public string[] OutputModes { get; } = { "clipboard", "type" };

    public string ToggleModeHotkey => "Ctrl+Alt+M";
    public string StopHotkey => "Ctrl+Alt+S";
    public string CopyLastOutputHotkey => "Ctrl+Alt+V";

    /// <summary>
    /// Called when the user clicks "Record Hotkey" to enter hotkey capture mode.
    /// </summary>
    [RelayCommand]
    private void StartRecordingHotkey()
    {
        IsRecordingHotkey = true;
    }

    /// <summary>
    /// Stops hotkey capture mode without saving.
    /// </summary>
    [RelayCommand]
    private void CancelHotkeyRecording()
    {
        IsRecordingHotkey = false;
    }

    /// <summary>
    /// Applies a captured hotkey combination and exits capture mode.
    /// Format: "Ctrl+Alt+D", "Ctrl+Shift+K", etc.
    /// </summary>
    public void SetHotkey(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return;
        HotkeyDisplay = hotkey;
        IsRecordingHotkey = false;
    }
}
