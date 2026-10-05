using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WhisperDictation.Avalonia.Services;

namespace WhisperDictation.Avalonia.ViewModels;

/// <summary>
/// Main ViewModel for the Avalonia controller.
/// Connects to the daemon via IPC and displays status, transcriptions, and settings.
/// Does NOT own the audio/whisper pipeline — that lives in the daemon.
/// </summary>
public partial class ControllerViewModel : ObservableObject, IDisposable
{
    private readonly IDaemonProxy _daemon;
    private readonly IDispatcherService _dispatcher;
    private readonly SettingsPersistenceService _settings;
    private readonly ILogger<ControllerViewModel> _logger;
    private bool _disposed;
    private bool _initialized;

    public GeneralSettingsViewModel General { get; }
    public AudioSettingsViewModel Audio { get; }
    public WhisperSettingsViewModel Whisper { get; }
    public LLMSettingsViewModel LLM { get; }
    public VocabularySettingsViewModel Vocabulary { get; }

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
    private string _daemonStateText = "Unknown";

    public ObservableCollection<TranscriptionResult> TranscriptionHistory { get; } = [];

    public string[] ActivationModes { get; } = { "Continuous", "Push-to-Talk" };

    public ControllerViewModel(
        IDaemonProxy daemon,
        IDispatcherService dispatcher,
        GeneralSettingsViewModel general,
        AudioSettingsViewModel audio,
        WhisperSettingsViewModel whisper,
        LLMSettingsViewModel llm,
        VocabularySettingsViewModel vocabulary,
        SettingsPersistenceService settings,
        ILogger<ControllerViewModel> logger)
    {
        _daemon = daemon;
        _dispatcher = dispatcher;
        _settings = settings;
        _logger = logger;

        General = general;
        Audio = audio;
        Whisper = whisper;
        LLM = llm;
        Vocabulary = vocabulary;

        _daemon.Disconnected += OnDaemonDisconnected;
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
        };
        _settings.Save(s);
    }

    public async Task InitializeAsync(IDaemonLifecycleService? lifecycleService = null)
    {
        // Guard against double-initialization (can happen if multiple lifecycle hooks fire)
        if (_initialized) return;
        _initialized = true;

        // Optionally ensure daemon is running before connecting
        if (lifecycleService != null)
        {
            DaemonStateText = lifecycleService.State.ToString();
            lifecycleService.StateChanged += (_, state) =>
                _dispatcher.BeginInvoke(() => DaemonStateText = state.ToString());

            var started = await lifecycleService.EnsureDaemonRunningAsync();
            if (!started)
            {
                _dispatcher.BeginInvoke(() =>
                {
                    IsConnected = false;
                    ConnectionStatus = "Daemon unavailable";
                    StatusText = "Cannot start daemon";
                    StatusColor = "#F44336";
                });
                return;
            }
        }

        try
        {
            await _daemon.ConnectAsync();
            _daemon.SubscribeToUpdates(OnDaemonMessage);
            _dispatcher.BeginInvoke(() =>
            {
                IsConnected = true;
                ConnectionStatus = "Connected";
                StatusText = "Connected to daemon";
            });
            _logger.LogInformation("Controller initialized and connected to daemon");
        }
        catch (Exception ex)
        {
            _dispatcher.BeginInvoke(() =>
            {
                IsConnected = false;
                ConnectionStatus = "Daemon not running";
                StatusText = "Cannot connect to daemon";
                StatusColor = "#F44336";
            });
            _logger.LogWarning(ex, "Failed to connect to daemon on startup");
        }
    }

    private void OnDaemonMessage(IpcMessage msg)
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
                    _logger.LogInformation("Settings changed notification from daemon");
                });
                break;
        }
    }

    private void OnDaemonDisconnected(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsConnected = false;
            ConnectionStatus = "Disconnected";
            StatusText = "Lost connection to daemon";
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
                await _daemon.SendCommandAsync(new StopListeningMessage());
            else
                await _daemon.SendCommandAsync(new StartListeningMessage());
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
            await _daemon.SendCommandAsync(new SetModeMessage { Mode = nextMode });
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
            await _daemon.SendCommandAsync(new GetHistoryMessage { Limit = 20 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh history");
        }
    }

    [RelayCommand]
    private async Task Reconnect()
    {
        if (_daemon.IsConnected) return;

        try
        {
            await _daemon.ConnectAsync();
            _daemon.SubscribeToUpdates(OnDaemonMessage);
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
        _daemon.Disconnected -= OnDaemonDisconnected;
        _daemon.Dispose();
        GC.SuppressFinalize(this);
    }
}
