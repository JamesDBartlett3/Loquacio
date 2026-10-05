namespace Loquacio.Avalonia.ViewModels;

/// <summary>
/// Whisper model settings tab for the controller.
/// </summary>
public partial class WhisperSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _modelPath = string.Empty;

    [ObservableProperty]
    private string _language = "auto";

    [ObservableProperty]
    private string _modelSize = "base";

    public string[] AvailableModels { get; } = { "tiny", "base", "small", "medium", "large-v3" };
    public string[] Languages { get; } = { "auto", "en", "es", "fr", "de", "ja", "zh", "pt", "it" };
}
