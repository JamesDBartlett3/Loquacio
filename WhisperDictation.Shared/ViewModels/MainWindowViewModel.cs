using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WhisperDictation.Models;
using WhisperDictation.Services;

namespace WhisperDictation.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IBackgroundTranscriptionService _transcriptionService;
    private readonly IAudioCaptureService _audioCapture;
    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IActivationManagerService _activationManager;
    private readonly ITrayIconService _trayIconService;
    private readonly IDispatcherService _dispatcher;
    private readonly ILogger<MainWindowViewModel> _logger;
    private bool _disposed;

    public GeneralTabViewModel General { get; }
    public AudioTabViewModel Audio { get; }
    public WhisperTabViewModel Whisper { get; }
    public LLMTabViewModel LLM { get; }
    public VocabularyTabViewModel Vocabulary { get; }

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _statusColor = "#808080";

    [ObservableProperty]
    private double _audioLevel;

    [ObservableProperty]
    private string _lastTranscription = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _currentActivationMode = "Continuous";

    public ObservableCollection<TranscriptionResult> TranscriptionHistory { get; } = new();

    public MainWindowViewModel(
        IBackgroundTranscriptionService transcriptionService,
        IAudioCaptureService audioCapture,
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IActivationManagerService activationManager,
        ITrayIconService trayIconService,
        IAutoStartService autoStartService,
        ILLMPostProcessorService llmPostProcessor,
        IVocabularyService vocabularyService,
        IModelManagerService modelManager,
        IDispatcherService dispatcher,
        ILogger<MainWindowViewModel> logger)
    {
        _transcriptionService = transcriptionService;
        _audioCapture = audioCapture;
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _activationManager = activationManager;
        _trayIconService = trayIconService;
        _dispatcher = dispatcher;
        _logger = logger;

        General = new GeneralTabViewModel(settingsService, hotkeyService, autoStartService);
        Audio = new AudioTabViewModel(audioCapture, settingsService);
        Whisper = new WhisperTabViewModel(settingsService, modelManager);
        LLM = new LLMTabViewModel(settingsService, llmPostProcessor);
        Vocabulary = new VocabularyTabViewModel(settingsService, vocabularyService);

        _transcriptionService.OnTranscriptionCompleted += OnTranscriptionCompleted;
        _transcriptionService.OnError += OnTranscriptionError;
        _audioCapture.OnAudioLevel += OnAudioLevel;

        _activationManager.ListeningStateChanged += OnListeningStateChanged;
        _activationManager.ModeChanged += OnActivationModeChanged;

        // Initialize activation manager (registers hotkeys, starts keyword detection)
        _ = Task.Run(async () =>
        {
            try
            {
                await _activationManager.InitializeAsync();
                _dispatcher.BeginInvoke(() =>
                {
                    CurrentActivationMode = _activationManager.CurrentMode.ToString();
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize activation manager");
            }
        });
    }

    private void OnAudioLevel(object? sender, double level)
    {
        _dispatcher.BeginInvoke(() => AudioLevel = level);
    }

    private void OnTranscriptionCompleted(object? sender, TranscriptionResult result)
    {
        _dispatcher.BeginInvoke(() =>
        {
            LastTranscription = result.Text;
            TranscriptionHistory.Insert(0, result);
            if (TranscriptionHistory.Count > 100)
                TranscriptionHistory.RemoveAt(TranscriptionHistory.Count - 1);

            StatusText = "Listening";
            StatusColor = "#2196F3";

            _trayIconService.UpdateState(TrayIconState.Listening);

            _ = Task.Run(async () =>
            {
                var settings = await _settingsService.GetSettingsAsync();
                if (settings.Tray.NotificationsEnabled)
                {
                    var preview = result.Text.Length > 50
                        ? result.Text[..50] + "..."
                        : result.Text;
                    _dispatcher.BeginInvoke(() =>
                    {
                        _trayIconService.ShowNotification("Transcription Complete", preview);
                    });
                }
            });
        });
    }

    private void OnTranscriptionError(object? sender, Exception ex)
    {
        _dispatcher.BeginInvoke(() =>
        {
            StatusText = "Error";
            StatusColor = "#F44336";
            _trayIconService.UpdateState(TrayIconState.Error);
            _logger.LogError(ex, "Transcription error");
        });
    }

    private void OnListeningStateChanged(object? sender, bool isListening)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsRunning = isListening;
            StatusText = isListening ? "Listening" : "Idle";
            StatusColor = isListening ? "#2196F3" : "#808080";
            _trayIconService.UpdateState(isListening ? TrayIconState.Listening : TrayIconState.Idle);
        });
    }

    private void OnActivationModeChanged(object? sender, ActivationMode mode)
    {
        _dispatcher.BeginInvoke(() =>
        {
            CurrentActivationMode = mode switch
            {
                ActivationMode.PushToTalk => "Push-to-Talk",
                ActivationMode.KeywordActivated => "Keyword",
                _ => "Continuous"
            };
        });
    }

    [RelayCommand]
    private async Task ToggleListening()
    {
        await _activationManager.ToggleListeningAsync();
    }

    [RelayCommand]
    private async Task SaveAllSettings()
    {
        await SaveAllSettingsAsync();
    }

    private async Task SaveAllSettingsAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        General.ApplyTo(settings.Activation);
        General.ApplyTo(settings.Output);
        General.ApplyTo(settings.Tray);
        Audio.ApplyTo(settings.Audio);
        Whisper.ApplyTo(settings.Whisper);
        LLM.ApplyTo(settings.LLM);
        Vocabulary.ApplyTo(settings.Vocabulary);
        await _settingsService.SaveSettingsAsync(settings);
        _logger.LogInformation("Settings saved");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _transcriptionService.OnTranscriptionCompleted -= OnTranscriptionCompleted;
        _transcriptionService.OnError -= OnTranscriptionError;
        _audioCapture.OnAudioLevel -= OnAudioLevel;
        _activationManager.ListeningStateChanged -= OnListeningStateChanged;
        _activationManager.ModeChanged -= OnActivationModeChanged;

        if (IsRunning)
        {
            _activationManager.ShutdownAsync().GetAwaiter().GetResult();
        }

        GC.SuppressFinalize(this);
    }
}
