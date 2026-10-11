using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Loquacio.Avalonia.Services;
using Loquacio.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Loquacio.Avalonia.ViewModels;

/// <summary>
/// Main ViewModel for the Avalonia controller.
/// Connects to the engine via IPC and displays status, transcriptions, and settings.
/// Does NOT own the audio/whisper pipeline — that lives in the engine.
/// </summary>
public partial class ControllerViewModel : ObservableObject, IDisposable
{
    private readonly IEngineProxy _engine;
    private readonly IDispatcherService _dispatcher;
    private readonly SettingsPersistenceService _settings;
    private readonly IAutoStartService? _autoStart;
    private readonly ILogger<ControllerViewModel> _logger;
    private bool _disposed;
    private bool _initialized;

    public GeneralSettingsViewModel General { get; }
    public AudioSettingsViewModel Audio { get; }
    public WhisperSettingsViewModel Whisper { get; }
    public LLMSettingsViewModel LLM { get; }
    public VocabularySettingsViewModel Vocabulary { get; }
    public HistoryViewModel History { get; }

    [ObservableProperty]
    private string _statusText = "Connecting…";

    [ObservableProperty]
    private string _statusColor = "#808080";

    [ObservableProperty]
    private double _audioLevel;

    [ObservableProperty]
    private string _lastTranscription = string.Empty;

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private string _currentMode = "Continuous";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private string _engineStateText = "Unknown";

    public ObservableCollection<TranscriptionResult> TranscriptionHistory { get; } = [];

    public string[] ActivationModes { get; } = { "Continuous", "Push-to-Talk" };

    public ControllerViewModel(
        IEngineProxy engine,
        IDispatcherService dispatcher,
        GeneralSettingsViewModel general,
        AudioSettingsViewModel audio,
        WhisperSettingsViewModel whisper,
        LLMSettingsViewModel llm,
        VocabularySettingsViewModel vocabulary,
        SettingsPersistenceService settings,
        ILogger<ControllerViewModel> logger,
        IAutoStartService? autoStart = null,
        HistoryViewModel? history = null)
    {
        _autoStart = autoStart;
        _engine = engine;
        _dispatcher = dispatcher;
        _settings = settings;
        _logger = logger;

        General = general;
        Audio = audio;
        Whisper = whisper;
        LLM = llm;
        Vocabulary = vocabulary;
        History = history!;

        _engine.Disconnected += OnEngineDisconnected;
    }

    public void LoadSettings()
    {
        var s = _settings.Load();
        if (s == null) return;

        General.ActivationMode = s.ActivationMode;
        General.HotkeyDisplay = s.HotkeyDisplay;
        General.KeywordEnabled = s.KeywordEnabled;
        General.Keyword = s.Keyword;
        General.OutputMode = s.OutputMode;
        General.NotificationsEnabled = s.NotificationsEnabled;
        General.StartWithWindows = s.StartWithWindows;

        Audio.SelectedDeviceId = s.SelectedDeviceId;
        Audio.Gain = s.Gain;
        Audio.SilenceThresholdMs = s.SilenceThresholdMs;

        Whisper.ModelSize = s.ModelSize;
        Whisper.Language = s.Language;

        LLM.Enabled = s.LlmEnabled;
        LLM.Provider = s.LlmProvider;
        LLM.Endpoint = s.LlmEndpoint;
        LLM.Model = s.LlmModel;

        Vocabulary.CustomWords.Clear();
        foreach (var w in s.CustomWords) Vocabulary.CustomWords.Add(w);

        MinimizeToTray = s.MinimizeToTray;

        _logger.LogInformation("Settings loaded from persistence");
    }

    public void SaveSettings()
    {
        var s = new AvaloniaSettings
        {
            ActivationMode = General.ActivationMode,
            HotkeyDisplay = General.HotkeyDisplay,
            KeywordEnabled = General.KeywordEnabled,
            Keyword = General.Keyword,
            OutputMode = General.OutputMode,
            NotificationsEnabled = General.NotificationsEnabled,
            SelectedDeviceId = Audio.SelectedDeviceId,
            Gain = Audio.Gain,
            SilenceThresholdMs = Audio.SilenceThresholdMs,
            ModelSize = Whisper.ModelSize,
            Language = Whisper.Language,
            LlmEnabled = LLM.Enabled,
            LlmProvider = LLM.Provider,
            LlmEndpoint = LLM.Endpoint,
            LlmModel = LLM.Model,
            CustomWords = Vocabulary.CustomWords.ToList(),
            MinimizeToTray = MinimizeToTray,
            StartWithWindows = General.StartWithWindows,
        };
        _settings.Save(s);

        // Phase 6.3 — keep the HKCU Run key in sync with the setting.
        if (_autoStart != null)
        {
            switch (AutoStartPolicy.ComputeTransition(General.StartWithWindows, _autoStart.IsEnabled))
            {
                case true: _autoStart.Enable(); break;
                case false: _autoStart.Disable(); break;
            }
        }

        // Parity with the WPF controller: push the user's changes to the engine
        // so they take effect immediately (settings live engine-side).
        _ = PushSettingsToEngineAsync();
    }

    /// <summary>
    /// Push the controller's current settings to the engine via IPC. Local values
    /// are merged onto the engine's live snapshot so engine-side-only settings
    /// (e.g. Whisper.ModelPath, Pipeline, Tray) are never clobbered.
    /// </summary>
    private async Task PushSettingsToEngineAsync()
    {
        if (_disposed) return;
        try
        {
            if (!_engine.IsConnected) return;
            var engineSettings = await _engine.GetSettingsAsync();
            if (engineSettings == null) return;

            engineSettings.Audio.DeviceId = Audio.SelectedDeviceId;
            engineSettings.Audio.Gain = Audio.Gain;
            engineSettings.Audio.SilenceThresholdMs = Audio.SilenceThresholdMs;
            engineSettings.Whisper.Language = Whisper.Language;
            engineSettings.LLM.Enabled = LLM.Enabled;
            engineSettings.LLM.Provider = LLM.Provider;
            engineSettings.LLM.Endpoint = LLM.Endpoint;
            engineSettings.LLM.Model = LLM.Model;
            engineSettings.Activation.Mode = ToEngineMode(General.ActivationMode);
            engineSettings.Activation.KeywordEnabled = General.KeywordEnabled;
            engineSettings.Activation.Keyword = General.Keyword;
            engineSettings.Activation.Hotkey = General.HotkeyDisplay;
            engineSettings.Vocabulary.CustomWords = Vocabulary.CustomWords.ToList();
            engineSettings.Output.Mode = General.OutputMode;

            var ack = await _engine.UpdateSettingsAsync(engineSettings);
            if (!ack.Success)
                _logger.LogWarning("Engine rejected settings update: {Error}", ack.Error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push settings to engine");
        }
    }

    private static string ToEngineMode(string? displayMode) =>
        displayMode?.Trim().ToLowerInvariant() switch
        {
            "push-to-talk" or "pushtotalk" or "ptt" => "push-to-talk",
            _ => "continuous",
        };

    public async Task InitializeAsync(IEngineLifecycleService? lifecycleService = null)
    {
        // Guard against double-initialization (can happen if multiple lifecycle hooks fire)
        if (_initialized) return;
        _initialized = true;

        // Optionally ensure engine is running before connecting
        if (lifecycleService != null)
        {
            EngineStateText = lifecycleService.State.ToString();
            lifecycleService.StateChanged += (_, state) =>
                _dispatcher.BeginInvoke(() => EngineStateText = state.ToString());

            var started = await lifecycleService.EnsureEngineRunningAsync();
            if (!started)
            {
                _dispatcher.BeginInvoke(() =>
                {
                    IsConnected = false;
                    ConnectionStatus = "Engine unavailable";
                    StatusText = "Cannot start engine";
                    StatusColor = "#F44336";
                });
                return;
            }
        }

        try
        {
            await _engine.ConnectAsync();
            _engine.SubscribeToUpdates(OnEngineMessage);
            _dispatcher.BeginInvoke(() =>
            {
                IsConnected = true;
                ConnectionStatus = "Connected";
                StatusText = "Connected to engine";
            });
            _logger.LogInformation("Controller initialized and connected to engine");
        }
        catch (Exception ex)
        {
            _dispatcher.BeginInvoke(() =>
            {
                IsConnected = false;
                ConnectionStatus = "Engine not running";
                StatusText = "Cannot connect to engine";
                StatusColor = "#F44336";
            });
            _logger.LogWarning(ex, "Failed to connect to engine on startup");
        }
    }

    private void OnEngineMessage(IpcMessage msg)
    {
        switch (msg)
        {
            case StatusUpdateMessage s:
                _dispatcher.BeginInvoke(() =>
                {
                    IsListening = s.IsListening;
                    StatusText = s.StatusText;
                    StatusColor = s.StatusColor;
                    CurrentMode = s.Mode.ToString();
                    AudioLevel = s.AudioLevel;
                });
                break;

            case TranscriptionResultMessage t:
                _dispatcher.BeginInvoke(() =>
                {
                    LastTranscription = t.Text;
                    var result = new TranscriptionResult
                    {
                        Text = t.Text,
                        Timestamp = t.Timestamp.LocalDateTime
                    };
                    TranscriptionHistory.Insert(0, result);
                    if (TranscriptionHistory.Count > 100)
                        TranscriptionHistory.RemoveAt(TranscriptionHistory.Count - 1);
                });
                break;

            case SettingsChangedMessage:
                _dispatcher.BeginInvoke(() =>
                {
                    _logger.LogInformation("Settings changed notification from engine");
                });
                break;
        }
    }

    private void OnEngineDisconnected(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsConnected = false;
            ConnectionStatus = "Disconnected";
            StatusText = "Lost connection to engine";
            StatusColor = "#F44336";
            IsListening = false;
        });
    }

    [RelayCommand]
    private async Task ToggleListening()
    {
        try
        {
            if (IsListening)
                await _engine.SendCommandAsync(new StopListeningMessage());
            else
                await _engine.SendCommandAsync(new StartListeningMessage());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle listening");
        }
    }

    [RelayCommand]
    private async Task CycleMode()
    {
        var nextMode = CurrentMode switch
        {
            "Continuous" => ActivationMode.PushToTalk,
            _ => ActivationMode.Continuous // keyword activation is disabled — two-way cycle
        };
        try
        {
            await _engine.SendCommandAsync(new SetModeMessage { Mode = nextMode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cycle mode");
        }
    }

    [RelayCommand]
    private async Task RefreshHistory()
    {
        try
        {
            await _engine.SendCommandAsync(new GetHistoryMessage { Limit = 20 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh history");
        }
    }

    [RelayCommand]
    private async Task Reconnect()
    {
        if (_engine.IsConnected) return;

        try
        {
            await _engine.ConnectAsync();
            _engine.SubscribeToUpdates(OnEngineMessage);
            _dispatcher.BeginInvoke(() =>
            {
                IsConnected = true;
                ConnectionStatus = "Connected";
                StatusText = "Reconnected";
                StatusColor = "#22c55e";
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconnect failed");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SaveSettings();
        _engine.Disconnected -= OnEngineDisconnected;
        _engine.Dispose();
        GC.SuppressFinalize(this);
    }
}
