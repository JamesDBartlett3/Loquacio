using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.ViewModels;

public partial class LLMTabViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILLMPostProcessorService _llmPostProcessor;

    [ObservableProperty]
    private string _provider = "lm-studio";

    [ObservableProperty]
    private string _endpoint = "http://localhost:1234/v1";

    [ObservableProperty]
    private string _model = "auto";

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private bool _isLlmEnabled = true;

    [ObservableProperty]
    private bool _isLlmAvailable;

    [ObservableProperty]
    private LlmModelInfo? _selectedModel;

    public ObservableCollection<LlmModelInfo> AvailableModels { get; } = [];

    public string[] Providers { get; } = { "lm-studio", "ollama", "auto-detect", "none" };

    public LLMTabViewModel(ISettingsService settingsService, ILLMPostProcessorService llmPostProcessor)
    {
        _settingsService = settingsService;
        _llmPostProcessor = llmPostProcessor;
        LoadAsync();
    }

    private async void LoadAsync()
    {
        _loading = true;
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            Provider = settings.LLM.Provider;
            Endpoint = settings.LLM.Endpoint;
            Model = settings.LLM.Model;
            IsLlmEnabled = settings.LLM.Enabled;
            IsLlmAvailable = _llmPostProcessor.IsAvailable;
            await LoadModelsAsync();
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnIsLlmEnabledChanged(bool value) => ScheduleAutoApply();
    partial void OnEndpointChanged(string value) => ScheduleAutoApply();
    partial void OnModelChanged(string value) => ScheduleAutoApply();
    private async Task LoadModelsAsync()
    {
        try
        {
            AvailableModels.Clear();
            foreach (var model in await _llmPostProcessor.GetAvailableModelsAsync())
                AvailableModels.Add(model);
            SelectedModel = AvailableModels.FirstOrDefault(m => m.Id == Model)
                ?? AvailableModels.FirstOrDefault();
            if (SelectedModel is not null) Model = SelectedModel.Id;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Models unavailable: {ex.Message}";
        }
    }

    partial void OnProviderChanged(string value)
    {
        Endpoint = value switch
        {
            "lm-studio" => "http://localhost:1234/v1",
            "ollama" => "http://localhost:11434/v1",
            "auto-detect" => "http://localhost:1234/v1",
            "none" => string.Empty,
            _ => Endpoint
        };

        IsLlmEnabled = value != "none";

        // The model list depends on the provider/endpoint — persist the new
        // provider first, then re-scan so only reachable models are offered.
        if (!_loading) _ = RescanModelsAsync();
    }

    private async Task RescanModelsAsync()
    {
        try
        {
            await SaveSettings();
        }
        catch (Exception ex)
        {
            _rescanError = ex.Message;
        }
        await LoadModelsAsync();
        if (_rescanError is not null)
        {
            ConnectionStatus = $"Save failed: {_rescanError}";
            _rescanError = null;
        }
    }

    private string? _rescanError;

    [RelayCommand]
    private async Task TestConnection()
    {
        IsTestingConnection = true;
        ConnectionStatus = "Testing...";

        try
        {
            var (success, message) = await _llmPostProcessor.TestConnectionAsync();
            ConnectionStatus = message;
            IsLlmAvailable = success;
            if (success) await LoadModelsAsync();

            // Also refresh the service state
            if (_llmPostProcessor is LLMPostProcessorService concrete)
            {
                await concrete.RefreshStateAsync();
                IsLlmEnabled = _llmPostProcessor.IsEnabled;
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"✗ {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private async Task AutoDetect()
    {
        IsTestingConnection = true;
        ConnectionStatus = "Detecting...";

        try
        {
            if (_llmPostProcessor is LLMPostProcessorService concrete)
            {
                var detected = await concrete.AutoDetectProviderAsync();

                if (detected != null)
                {
                    Provider = detected;
                    Endpoint = detected switch
                    {
                        "lm-studio" => "http://localhost:1234/v1",
                        "ollama" => "http://localhost:11434/v1",
                        _ => Endpoint
                    };
                    ConnectionStatus = $"✓ Detected: {detected}";
                    IsLlmAvailable = true;
                }
                else
                {
                    ConnectionStatus = "✗ No LLM provider detected";
                    IsLlmAvailable = false;
                }
            }
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    public void ApplyTo(LLMSettings llm)
    {
        llm.Provider = Provider;
        llm.Endpoint = Endpoint;
        llm.Model = Model;
        llm.Enabled = IsLlmEnabled;
    }

    partial void OnSelectedModelChanged(LlmModelInfo? value)
    {
        if (value is not null)
        {
            Model = value.Id;
            ScheduleAutoApply();
        }
    }

    public event EventHandler? SettingsSaved;


    // ── Auto-apply: any setting change is saved (debounced) and pushed ──

    private CancellationTokenSource? _autoApplyCts;
    private bool _loading;

    /// <summary>Debounced save-and-push; call from On*Changed partials.</summary>
    protected void ScheduleAutoApply()
    {
        if (_loading) return;

        _autoApplyCts?.Cancel();
        _autoApplyCts = new CancellationTokenSource();
        var ct = _autoApplyCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, ct);
                if (ct.IsCancellationRequested) return;
                await SaveSettings();
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        var settings = await _settingsService.GetSettingsAsync();
        ApplyTo(settings.LLM);
        await _settingsService.SaveSettingsAsync(settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }
}
