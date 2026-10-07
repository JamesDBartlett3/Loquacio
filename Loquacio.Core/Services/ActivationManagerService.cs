using Microsoft.Extensions.Logging;
using Loquacio.Models;

namespace Loquacio.Services;

/// <summary>
/// Implementation of the activation manager (WPF-specific parts removed, uses platform-agnostic types)
/// </summary>
public class ActivationManagerService : IActivationManagerService
{
    private readonly IHotkeyService _hotkeyService;
    private readonly IKeywordDetectionService _keywordDetection;
    private readonly IBackgroundTranscriptionService _transcriptionService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ActivationManagerService> _logger;

    private ActivationMode _currentMode = ActivationMode.Continuous;
    private bool _isListening;
    private bool _disposed;
    private bool _pushToTalkActive;

    public ActivationMode CurrentMode => _currentMode;
    public bool IsListening => _isListening;

    public event EventHandler<bool>? ListeningStateChanged;
    public event EventHandler<ActivationMode>? ModeChanged;

    public ActivationManagerService(
        IHotkeyService hotkeyService,
        IKeywordDetectionService keywordDetection,
        IBackgroundTranscriptionService transcriptionService,
        ISettingsService settingsService,
        ILogger<ActivationManagerService> logger)
    {
        _hotkeyService = hotkeyService;
        _keywordDetection = keywordDetection;
        _transcriptionService = transcriptionService;
        _settingsService = settingsService;
        _logger = logger;

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _keywordDetection.KeywordDetected += OnKeywordDetected;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        _currentMode = ParseMode(settings.Activation.Mode);

        // Keyword activation is disabled (the energy-based spotter can't actually
        // recognize words and its toggling was more harm than help). If a stale
        // setting still requests it, fall back to Continuous and say so.
        if (settings.Activation.KeywordEnabled)
            _logger.LogWarning("Keyword activation is disabled — ignoring keyword settings (mode: {Mode})", _currentMode);

        _logger.LogInformation("Activation manager initialized — mode: {Mode}", _currentMode);
    }

    // Global hotkey registration is owned by the engine's HotkeyManager (Win32/X11);
    // the legacy IHotkeyService here is a no-op on the engine.

    private async void OnHotkeyPressed(object? sender, string action)
    {
        try
        {
            switch (action)
            {
                case HotkeyActions.ToggleListening:
                    if (_currentMode == ActivationMode.PushToTalk)
                    {
                        if (_pushToTalkActive)
                            await OnPushToTalkEndAsync();
                        else
                            await OnPushToTalkStartAsync();
                    }
                    else
                    {
                        await ToggleListeningAsync();
                    }
                    break;

                case HotkeyActions.ToggleMode:
                    var newMode = _currentMode == ActivationMode.Continuous
                        ? ActivationMode.PushToTalk
                        : ActivationMode.Continuous;
                    await SwitchModeAsync(newMode);
                    break;

                case HotkeyActions.Stop:
                    if (_isListening)
                    {
                        await _transcriptionService.StopAsync();
                        SetListening(false);
                    }
                    break;

                case HotkeyActions.PushToTalk:
                    if (_currentMode == ActivationMode.PushToTalk)
                    {
                        if (_pushToTalkActive)
                            await OnPushToTalkEndAsync();
                        else
                            await OnPushToTalkStartAsync();
                    }
                    break;

                case HotkeyActions.CopyLastOutput:
                    _logger.LogDebug("Copy last output hotkey pressed");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling hotkey action {Action}", action);
        }
    }

    private async void OnKeywordDetected(object? sender, EventArgs e)
    {
        // Defensive: keyword detection is never started while the feature is
        // disabled, so this should not fire.
        _logger.LogDebug("Keyword detected — ignored (keyword activation is disabled)");
    }

    public async Task SwitchModeAsync(ActivationMode mode, CancellationToken ct = default)
    {
        // Keyword activation is disabled — refuse the switch instead of
        // entering a mode that can never be driven by a working detector.
        if (mode == ActivationMode.KeywordActivated)
        {
            _logger.LogWarning("SwitchModeAsync: keyword activation is disabled — ignoring request");
            return;
        }

        if (_currentMode == mode) return;

        _logger.LogInformation("Switching activation mode: {Old} → {New}", _currentMode, mode);

        if (_isListening)
        {
            await _transcriptionService.StopAsync();
            SetListening(false);
        }

        _currentMode = mode;
        ModeChanged?.Invoke(this, mode);

        var settings = await _settingsService.GetSettingsAsync(ct);
        settings.Activation.Mode = mode.ToString().ToLowerInvariant();
        await _settingsService.SaveSettingsAsync(settings, ct);
    }

    public async Task CycleModeAsync(CancellationToken ct = default)
    {
        // Keyword activation is disabled — two-way cycle only
        var next = _currentMode == ActivationMode.Continuous
            ? ActivationMode.PushToTalk
            : ActivationMode.Continuous;
        await SwitchModeAsync(next, ct);
    }

    public async Task StopListeningAsync(CancellationToken ct = default)
    {
        if (!_isListening) return;
        await _transcriptionService.StopAsync();
        SetListening(false);
    }

    public async Task ToggleListeningAsync(CancellationToken ct = default)
    {
        if (_isListening)
        {
            await _transcriptionService.StopAsync();
            SetListening(false);
        }
        else
        {
            await StartListeningAsync(ct);
        }
    }

    private async Task StartListeningAsync(CancellationToken ct = default)
    {
        try
        {
            await _transcriptionService.StartAsync(ct);
            SetListening(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start listening");
            throw;
        }
    }

    public async Task OnPushToTalkStartAsync(CancellationToken ct = default)
    {
        if (_pushToTalkActive) return;
        _pushToTalkActive = true;

        if (!_isListening)
            await StartListeningAsync(ct);

        _logger.LogDebug("Push-to-talk started");
    }

    public async Task OnPushToTalkEndAsync(CancellationToken ct = default)
    {
        if (!_pushToTalkActive) return;
        _pushToTalkActive = false;

        if (_isListening)
        {
            await _transcriptionService.StopAsync();
            SetListening(false);
        }

        _logger.LogDebug("Push-to-talk ended");
    }

    private void SetListening(bool listening)
    {
        if (_isListening == listening) return;
        _isListening = listening;
        ListeningStateChanged?.Invoke(this, listening);
        _logger.LogInformation("Listening state: {State}", listening ? "ACTIVE" : "INACTIVE");
    }

    private static ActivationMode ParseMode(string mode) => mode.ToLowerInvariant() switch
    {
        "push-to-talk" or "pushtotalk" or "ptt" => ActivationMode.PushToTalk,
        _ => ActivationMode.Continuous // "keyword" falls back to Continuous (keyword activation is disabled)
    };

    public async Task ApplyActivationSettingsAsync(CancellationToken ct = default)
    {
        // Keyword activation is disabled — the keyword detector is never started,
        // so there is nothing to reconfigure here anymore.
        await Task.CompletedTask;
    }

    public async Task ShutdownAsync()
    {
        if (_isListening)
        {
            await _transcriptionService.StopAsync();
            SetListening(false);
        }

        await _keywordDetection.StopAsync();
        _hotkeyService.UnregisterAll();
        _logger.LogInformation("Activation manager shut down");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _keywordDetection.KeywordDetected -= OnKeywordDetected;

        ShutdownAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
