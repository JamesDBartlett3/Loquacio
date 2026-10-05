namespace WhisperDictation.Avalonia.ViewModels;

/// <summary>
/// Audio settings tab for the controller.
/// Shows current daemon audio configuration (read-only display in controller mode).
/// </summary>
public partial class AudioSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _selectedDeviceId = "default";

    [ObservableProperty]
    private double _gain = 1.0;

    [ObservableProperty]
    private double _silenceThresholdMs = 1500;

    [ObservableProperty]
    private double _currentLevel;

    /// <summary>
    /// VU meter fill percentage (0.0–1.0).
    /// </summary>
    public double VuMeterFill => Math.Clamp(CurrentLevel, 0, 1);
}
