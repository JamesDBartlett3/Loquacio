namespace Loquacio.Avalonia.ViewModels;

/// <summary>
/// LLM post-processing settings tab for the controller.
/// </summary>
public partial class LLMSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _provider = "lm-studio";

    [ObservableProperty]
    private string _endpoint = "http://localhost:1234/v1";

    [ObservableProperty]
    private string _model = "auto";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _connectionStatus = "Not tested";

    public string[] Providers { get; } = { "lm-studio", "ollama", "auto-detect" };
}
