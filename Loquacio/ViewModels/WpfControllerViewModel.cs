using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Loquacio.Ipc;
using Loquacio.Models;

namespace Loquacio.ViewModels;

/// <summary>
/// Main ViewModel for the WPF controller.
/// Connects to the daemon via IPC and displays status, transcriptions, and settings.
/// Does NOT own the audio/whisper pipeline — that lives in the daemon.
/// This replaces the old <see cref="MainWindowViewModel"/> which directly owned services.
/// </summary>
public partial class WpfControllerViewModel : ObservableObject, IDisposable
{
    private readonly IDaemonProxy _daemon;
    private readonly IDispatcherService _dispatcher;
    private readonly WpfSettingsPersistenceService _settings;
    private readonly ISettingsService _daemonSettingsService;
    private readonly WpfDaemonLifecycleService? _lifecycle;
    private readonly Loquacio.Daemon.Services.InProcessDaemonHost? _inProcessDaemonHost;
    private readonly ITrayIconService _trayIcon;
    private readonly IAutoStartService? _autoStart;
    private readonly ILogger<WpfControllerViewModel> _logger;
    private bool _disposed;
    private bool _initialized;

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

    /// <summary>Two-way mode switch on the dashboard: true = Push-to-Talk, false = Continuous.</summary>
    [ObservableProperty]
    private bool _isPushToTalk;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private string _daemonStateText = "Unknown";

    [ObservableProperty]
    private bool _autoStartDaemon = true;

    [ObservableProperty]
    private bool _inProcessDaemon;

    [ObservableProperty]
    private bool _startMinimized;

    /// <summary>Start with Windows — backed by the registry Run key via IAutoStartService.</summary>
    [ObservableProperty]
    private bool _startWithWindows;

    partial void OnMinimizeToTrayChanged(bool value) => SaveSettings();
    partial void OnCloseToTrayChanged(bool value) => SaveSettings();
    partial void OnStartMinimizedChanged(bool value) => SaveSettings();
    partial void OnAutoStartDaemonChanged(bool value) => SaveSettings();

    partial void OnStartWithWindowsChanged(bool value)
    {
        // Apply immediately; the registry Run key is the source of truth.
        if (_autoStart is null) return;
        if (value && !_autoStart.IsEnabled) _autoStart.Enable();
        else if (!value && _autoStart.IsEnabled) _autoStart.Disable();
    }

    /// <summary>UI theme: System / Light / Dark (applied immediately on change).</summary>
    [ObservableProperty]
    private string _themeMode = "System";

    public string[] ThemeModeOptions { get; } = { "System", "Light", "Dark" };

    partial void OnThemeModeChanged(string value)
    {
        // ThemeMode is a (non-enum) immutable type with static instances; the
        // setter live-swaps the Fluent theme across all open windows.
        var mode = value.Trim().ToLowerInvariant() switch
        {
            "light" => System.Windows.ThemeMode.Light,
            "dark" => System.Windows.ThemeMode.Dark,
            _ => System.Windows.ThemeMode.System,
        };
        System.Windows.Application.Current.ThemeMode = mode;
        SaveSettings();
    }

    /// <summary>⚠ banner: true when no Whisper model is configured on the daemon.</summary>
    [ObservableProperty]
    private bool _noModelConfigured = true;

    /// <summary>Tab ViewModels for daemon-backed settings (Audio/Whisper/Activation).</summary>
    [ObservableProperty]
    private AudioTabViewModel? _audioSettings;

    [ObservableProperty]
    private WhisperTabViewModel? _whisperSettings;

    [ObservableProperty]
    private GeneralTabViewModel? _activationSettings;

    [ObservableProperty]
    private VocabularyTabViewModel? _vocabularySettings;

    [ObservableProperty]
    private LLMTabViewModel? _llmSettings;

    [ObservableProperty]
    private double _peakAudioLevel;

    /// <summary>
    /// Attach the settings-tab ViewModels and wire their save events so changes
    /// round-trip to the daemon via IPC (settings actually take effect there).
    /// </summary>
    public void AttachSettingsViewModels(
        AudioTabViewModel audio,
        WhisperTabViewModel whisper,
        GeneralTabViewModel activation,
        VocabularyTabViewModel vocabulary,
        LLMTabViewModel llm)
    {
        AudioSettings = audio;
        WhisperSettings = whisper;
        ActivationSettings = activation;
        VocabularySettings = vocabulary;
        LlmSettings = llm;

        audio.SettingsSaved += OnSettingsSaved;
        whisper.SettingsSaved += OnSettingsSaved;
        activation.SettingsSaved += OnSettingsSaved;
        vocabulary.SettingsSaved += OnSettingsSaved;
        LlmSettings.SettingsSaved += OnSettingsSaved;

        // Keep the tray menu's LLM toggle in sync with the LLM tab
        LlmSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LLMTabViewModel.IsLlmEnabled))
                _dispatcher.BeginInvoke(() =>
                    _trayIcon.UpdateMenuState(IsPushToTalk, LlmSettings.IsLlmEnabled));
        };
    }

    private async void OnSettingsSaved(object? sender, EventArgs e)
    {
        try
        {
            // The tab VM already persisted to the local settings store; push the
            // same settings to the daemon so they take effect there immediately.
            if (_daemon is DaemonProxy proxy && proxy.IsConnected)
            {
                var settings = await _daemonSettingsService.GetSettingsAsync();
                var (section, value) = sender switch
                {
                    AudioTabViewModel => ("audio", (object)settings.Audio),
                    WhisperTabViewModel => ("whisper", settings.Whisper),
                    GeneralTabViewModel => ("activation", settings.Activation),
                    VocabularyTabViewModel => ("vocabulary", settings.Vocabulary),
                    LLMTabViewModel => ("llm", settings.LLM),
                    _ => (string.Empty, (object?)null)
                };
                var ack = string.IsNullOrEmpty(section)
                    ? await proxy.UpdateSettingsAsync(settings)
                    : await proxy.UpdateSettingsSectionAsync(section, value!);
                if (!ack.Success)
                    _logger.LogWarning("Daemon rejected settings update: {Error}", ack.Error);
                else if (section == "whisper")
                    RefreshModelStatus(settings);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push settings to daemon");
        }
    }

    /// <summary>Update the no-model banner from a settings snapshot.</summary>
    public void RefreshModelStatus(Settings settings)
    {
        _dispatcher.BeginInvoke(() =>
            NoModelConfigured = string.IsNullOrWhiteSpace(settings.Whisper.ModelPath));
    }

    public ObservableCollection<TranscriptionResult> TranscriptionHistory { get; } = [];

    public WpfControllerViewModel(
        IDaemonProxy daemon,
        IDispatcherService dispatcher,
        WpfSettingsPersistenceService settings,
        ITrayIconService trayIcon,
        ILogger<WpfControllerViewModel> logger,
        WpfDaemonLifecycleService? lifecycle = null,
        Loquacio.Daemon.Services.InProcessDaemonHost? inProcessDaemon = null,
        ISettingsService? daemonSettingsService = null,
        IAutoStartService? autoStart = null)
    {
        _daemon = daemon;
        _dispatcher = dispatcher;
        _settings = settings;
        _trayIcon = trayIcon;
        _autoStart = autoStart;
        _logger = logger;
        _lifecycle = lifecycle;
        _inProcessDaemonHost = inProcessDaemon;
        _daemonSettingsService = daemonSettingsService ?? new SettingsService(NullLoggerFactory.Instance.CreateLogger<SettingsService>());
        _startWithWindows = autoStart is not null && autoStart.IsEnabled;

        _daemon.Disconnected += OnDaemonDisconnected;
    }

    public void LoadSettings()
    {
        var s = _settings.Load();
        if (s == null) return;

        MinimizeToTray = s.MinimizeToTray;
        CloseToTray = s.CloseToTray;
        StartMinimized = s.StartMinimized;
        AutoStartDaemon = s.AutoStartDaemon;
        InProcessDaemon = s.InProcessDaemon;
        ThemeMode = s.ThemeMode;

        _logger.LogInformation("Settings loaded from persistence");
    }

    public void SaveSettings()
    {
        var s = new WpfControllerSettings
        {
            MinimizeToTray = MinimizeToTray,
            CloseToTray = CloseToTray,
            StartMinimized = StartMinimized,
            AutoStartDaemon = AutoStartDaemon,
            InProcessDaemon = InProcessDaemon,
            ThemeMode = ThemeMode,
        };
        _settings.Save(s);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // Optionally ensure daemon is running before connecting
        if (AutoStartDaemon && _inProcessDaemonHost != null && InProcessDaemon)
        {
            // In-process mode: host the daemon inside this process, unless an
            // external daemon is already serving the IPC endpoint.
            var externalRunning = _lifecycle != null && await _lifecycle.IsDaemonRunningAsync();
            if (!externalRunning)
            {
                try
                {
                    await _inProcessDaemonHost.StartAsync();
                    DaemonStateText = "Running";
                    _logger.LogInformation("In-process background service started ({Endpoint})", _inProcessDaemonHost.Endpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start in-process background service");
                    _dispatcher.BeginInvoke(() =>
                    {
                        IsConnected = false;
                        ConnectionStatus = "Background service unavailable";
                        StatusText = "Cannot start background service";
                        StatusColor = "#F44336";
                        _trayIcon.UpdateState(TrayIconState.Error);
                    });
                    return;
                }
            }
            else
            {
                DaemonStateText = "Running";
            }
        }
        else if (_lifecycle != null && AutoStartDaemon)
        {
            DaemonStateText = DescribeServiceState(_lifecycle.State.ToString());
            _lifecycle.StateChanged += (_, state) =>
                _dispatcher.BeginInvoke(() => DaemonStateText = DescribeServiceState(state.ToString()));

            var started = await _lifecycle.EnsureDaemonRunningAsync();
            if (!started)
            {
                _dispatcher.BeginInvoke(() =>
                {
                    IsConnected = false;
                    ConnectionStatus = "Background service unavailable";
                    StatusText = "Cannot start background service";
                    StatusColor = "#F44336";
                    _trayIcon.UpdateState(TrayIconState.Error);
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
                StatusText = "Connected";
                StatusColor = "#22c55e";
                _trayIcon.UpdateState(TrayIconState.Idle);
            });

            // Reflect daemon-side model status in the dashboard banner
            if (_daemon is DaemonProxy p && p.IsConnected)
            {
                var snapshot = await p.GetSettingsAsync();
                var localSettings = await _daemonSettingsService.GetSettingsAsync();
                if (!string.IsNullOrWhiteSpace(localSettings.Whisper.ModelPath)
                    && !string.Equals(localSettings.Whisper.ModelPath,
                        snapshot?.Whisper.ModelPath, StringComparison.OrdinalIgnoreCase))
                {
                    var ack = await p.UpdateSettingsSectionAsync("whisper", localSettings.Whisper);
                    if (!ack.Success)
                        _logger.LogWarning("Failed to synchronize model settings with daemon: {Error}", ack.Error);
                    else
                        snapshot = await p.GetSettingsAsync();
                }
                if (snapshot is not null)
                {
                    RefreshModelStatus(snapshot);
                    // Sync the mode drop-down with the daemon's persisted mode on connect
                    ActivationSettings?.ApplyDaemonMode(snapshot.Activation.Mode);
                }
            }

            // Start health monitoring if lifecycle service is available (external daemon mode only;
            // in-process mode is supervised directly by RestartDaemon/Dispose)
            if (!InProcessDaemon)
                _lifecycle?.StartHealthMonitoring();

            _logger.LogInformation("Controller initialized and connected to daemon");
        }
        catch (Exception ex)
        {
            _dispatcher.BeginInvoke(() =>
            {
                IsConnected = false;
                ConnectionStatus = "Background service not running";
                StatusText = "Cannot connect to background service";
                StatusColor = "#F44336";
                _trayIcon.UpdateState(TrayIconState.Error);
            });
            _logger.LogWarning(ex, "Failed to connect to background service on startup");
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
                    // ActivationMode.ToString() gives "PushToTalk" — normalize for
                    // display and for the mode-switch binding (which compares exact strings)
                    CurrentMode = s.Mode switch
                    {
                        ActivationMode.PushToTalk => "Push-to-Talk",
                        _ => "Continuous",
                    };
                    // Keep the Activation & Hotkeys drop-down in sync with the live mode
                    ActivationSettings?.ApplyDaemonMode(s.Mode switch
                    {
                        ActivationMode.PushToTalk => "push-to-talk",
                        _ => "continuous",
                    });
                    _trayIcon.UpdateMenuState(IsPushToTalk, LlmSettings?.IsLlmEnabled ?? true);
                    StatusText = s.StatusText;
                    StatusColor = s.StatusColor;
                    AudioLevel = s.AudioLevel;
                    PeakAudioLevel = s.PeakAudioLevel;
                    if (AudioSettings is not null)
                    {
                        AudioSettings.CurrentAudioLevel = s.AudioLevel;
                        AudioSettings.PeakAudioLevel = s.PeakAudioLevel;
                    }

                    // Update tray icon to reflect daemon state
                    _trayIcon.UpdateState(s.IsListening ? TrayIconState.Listening : TrayIconState.Idle);
                    _trayIcon.UpdateTooltip($"Loquacio - {(s.IsListening ? "Listening" : "Idle")}");
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

                    _trayIcon.UpdateState(TrayIconState.Processing);
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
            StatusText = "Lost connection to background service";
            StatusColor = "#F44336";
            IsListening = false;
            _trayIcon.UpdateState(TrayIconState.Error);
        });
    }

    [RelayCommand]
    private async Task ToggleListening()
    {
        try
        {
            if (IsListening)
            {
                var ack = await _daemon.SendCommandAsync(new StopListeningMessage());
                if (!ack.Success)
                    ShowCommandError(ack.Error ?? "Failed to stop listening");
            }
            else
            {
                var ack = await _daemon.SendCommandAsync(new StartListeningMessage());
                if (!ack.Success)
                    ShowCommandError(ack.Error ?? "Failed to start listening");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle listening");
            ShowCommandError(ex.Message);
        }
    }

    private void ShowCommandError(string message)
    {
        _dispatcher.BeginInvoke(() =>
        {
            StatusText = message;
            StatusColor = "#F44336";
            _trayIcon.UpdateState(TrayIconState.Error);
        });
    }

    partial void OnCurrentModeChanged(string value)
    {
        // Keep the dashboard toggle switch in sync with the daemon-reported mode
        var isPtt = value == "Push-to-Talk";
        if (IsPushToTalk != isPtt) IsPushToTalk = isPtt;
    }

    partial void OnIsPushToTalkChanged(bool value)
    {
        if (!IsConnected) return; // daemon will confirm the mode via status updates
        _ = SetModeAsync(value);
    }

    private async Task SetModeAsync(bool pushToTalk)
    {
        try
        {
            await _daemon.SendCommandAsync(new SetModeMessage
            {
                Mode = pushToTalk ? ActivationMode.PushToTalk : ActivationMode.Continuous
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set mode");
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

    [RelayCommand]
    private async Task RestartDaemon()
    {
        // In-process mode: restart the hosted daemon
        if (InProcessDaemon && _inProcessDaemonHost != null)
        {
            try
            {
                await _inProcessDaemonHost.StopAsync();
                await _inProcessDaemonHost.StartAsync();
                DaemonStateText = "Running";
                await Reconnect();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart in-process daemon");
            }
            return;
        }

        if (_lifecycle == null) return;

        try
        {
            var success = await _lifecycle.RestartDaemonAsync();
            if (success)
            {
                await _daemon.ConnectAsync();
                _daemon.SubscribeToUpdates(OnDaemonMessage);
                _dispatcher.BeginInvoke(() =>
                {
                    IsConnected = true;
                    ConnectionStatus = "Connected";
                    StatusText = "Background service restarted";
                    StatusColor = "#22c55e";
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart daemon");
        }
    }

    private static string DescribeServiceState(string raw) => raw switch
    {
        "Running" => "Running",
        "Starting" => "Starting…",
        "Restarting" => "Restarting…",
        "Unhealthy" => "Reconnecting…",
        "Stopped" => "Stopped",
        _ => "—",
    };

    /// <summary>
    /// Stops the background service when the controller exits — the UI and the
    /// service launch and quit together.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_inProcessDaemonHost is { IsRunning: true })
        {
            try { await _inProcessDaemonHost.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to stop in-process background service"); }
            return;
        }

        if (_lifecycle is not null)
        {
            try { await _lifecycle.StopDaemonAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to stop background service on exit"); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SaveSettings();
        _daemon.Disconnected -= OnDaemonDisconnected;
        if (AudioSettings is not null) AudioSettings.SettingsSaved -= OnSettingsSaved;
        if (WhisperSettings is not null) WhisperSettings.SettingsSaved -= OnSettingsSaved;
        if (ActivationSettings is not null) ActivationSettings.SettingsSaved -= OnSettingsSaved;
        if (VocabularySettings is not null) VocabularySettings.SettingsSaved -= OnSettingsSaved;
        if (LlmSettings is not null) LlmSettings.SettingsSaved -= OnSettingsSaved;
        _lifecycle?.Dispose();
        if (_inProcessDaemonHost is { IsRunning: true })
        {
            _ = _inProcessDaemonHost.StopAsync();
        }
        _daemon.Dispose();
        GC.SuppressFinalize(this);
    }
}
