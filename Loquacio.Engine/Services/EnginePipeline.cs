using Microsoft.Extensions.Logging;
using Loquacio.Engine.Ipc;
using Loquacio.Infrastructure;

namespace Loquacio.Engine.Services;

/// <summary>
/// Orchestrates the full audio pipeline: capture → Whisper → LLM post-processing → text injection.
/// Runs in the engine process, independent of any controller.
/// </summary>
public sealed class EnginePipeline(
    IActivationManagerService activationManager,
    IBackgroundTranscriptionService transcriptionService,
    IHistoryService historyService,
    ILLMPostProcessorService llmPostProcessor,
    IpcServer ipcServer,
    ILogger<EnginePipeline> logger
)
{
    private readonly ILLMPostProcessorService _llmPostProcessor = llmPostProcessor;

    private CancellationTokenSource? _cts;
    private bool _initialized;

    public bool IsRunning => activationManager.IsListening;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Wire transcription results → history → IPC broadcast.
        // (Text output — clipboard copy or keystroke injection into the focused
        // window — is handled by BackgroundTranscriptionService per Output settings.)
        transcriptionService.OnTranscriptionCompleted += OnTranscriptionCompleted;
        transcriptionService.OnError += OnTranscriptionError;
        transcriptionService.OnAudioLevel += OnAudioLevel;
        transcriptionService.PipelineStateChanged += OnPipelineStateChanged;

        // Subscribe to activation state changes for status broadcasts
        activationManager.ListeningStateChanged += OnListeningStateChanged;
        activationManager.ModeChanged += OnModeChanged;

        await activationManager.InitializeAsync(_cts.Token);

        _initialized = true;
        logger.LogInformation("Engine pipeline initialized");
    }

    public async Task ShutdownAsync()
    {
        if (!_initialized) return;

        transcriptionService.OnTranscriptionCompleted -= OnTranscriptionCompleted;
        transcriptionService.OnError -= OnTranscriptionError;
        transcriptionService.OnAudioLevel -= OnAudioLevel;
        transcriptionService.PipelineStateChanged -= OnPipelineStateChanged;
        activationManager.ListeningStateChanged -= OnListeningStateChanged;
        activationManager.ModeChanged -= OnModeChanged;

        await activationManager.ShutdownAsync();

        _cts?.Cancel();
        _initialized = false;
        logger.LogInformation("Engine pipeline shut down");
    }

    private async void OnTranscriptionCompleted(object? sender, TranscriptionResult result)
    {
        try
        {
            logger.LogInformation("Transcription completed: {Text}…", result.Text[..Math.Min(60, result.Text.Length)]);

            // Record in history
            await historyService.AddEntryAsync(new HistoryEntry
            {
                Id = Guid.NewGuid(),
                RawText = result.RawText ?? result.Text,
                CorrectedText = result.Text,
                Model = "whisper",
                Language = result.Language ?? "auto",
                Timestamp = DateTime.UtcNow,
                DurationMs = (long)result.Duration.TotalMilliseconds,
                LlmProcessed = result.IsPostProcessed
            });

            // Broadcast to connected controllers
            await ipcServer.BroadcastMessageAsync(new TranscriptionResultMessage
            {
                Text = result.Text,
                Timestamp = DateTimeOffset.UtcNow,
                IsFinal = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling transcription result");
        }
    }

    private async void OnTranscriptionError(object? sender, Exception ex)
    {
        logger.LogError(ex, "Transcription pipeline error");
        try
        {
            await ipcServer.BroadcastMessageAsync(new StatusUpdateMessage
            {
                IsListening = activationManager.IsListening,
                Mode = activationManager.CurrentMode,
                StatusText = $"Audio error: {ex.Message}",
                StatusColor = "#F44336"
            });
        }
        catch (Exception broadcastException)
        {
            logger.LogError(broadcastException, "Error broadcasting pipeline failure");
        }
    }

    private DateTime _lastLevelBroadcast = DateTime.MinValue;
    private readonly object _levelLock = new();
    private readonly Queue<(DateTime Timestamp, double Level)> _recentLevels = new();

    private async void OnAudioLevel(object? sender, double level)
    {
        // Throttle: audio level callbacks arrive every few ms; controllers only
        // need ~10 updates/second for a usable VU meter.
        var now = DateTime.UtcNow;
        if ((now - _lastLevelBroadcast).TotalMilliseconds < 100) return;
        _lastLevelBroadcast = now;

        try
        {
            double peak;
            lock (_levelLock)
            {
                _recentLevels.Enqueue((now, level));
                while (_recentLevels.Count > 0 && (now - _recentLevels.Peek().Timestamp).TotalSeconds > 5)
                    _recentLevels.Dequeue();
                peak = _recentLevels.Max(x => x.Level);
            }
            await ipcServer.BroadcastMessageAsync(new StatusUpdateMessage
            {
                IsListening = activationManager.IsListening,
                Mode = activationManager.CurrentMode,
                AudioLevel = level,
                PeakAudioLevel = peak,
                StatusText = activationManager.IsListening ? "Listening" : "Idle",
                StatusColor = activationManager.IsListening ? "#22c55e" : "#808080"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error broadcasting audio level");
        }
    }

    private async void OnPipelineStateChanged(DictationStateChangedEventArgs e)
    {
        // Raised under the FSM lock — do not call back into the FSM here.
        try
        {
            var (text, color) = PipelineStatusStyle.Describe(e.To, activationManager.IsListening);
            await ipcServer.BroadcastMessageAsync(new StatusUpdateMessage
            {
                IsListening = activationManager.IsListening,
                Mode = activationManager.CurrentMode,
                StatusText = text,
                StatusColor = color,
                PipelineState = e.To.ToString(),
                SessionId = e.SessionId.ToString()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error broadcasting pipeline state change");
        }
    }

    private async void OnListeningStateChanged(object? sender, bool isListening)
    {
        try
        {
            // Broadcast status update to all connected controllers
            await ipcServer.BroadcastMessageAsync(new StatusUpdateMessage
            {
                IsListening = isListening,
                Mode = activationManager.CurrentMode,
                StatusText = isListening ? "Listening" : "Idle",
                StatusColor = isListening ? "#22c55e" : "#808080"
            });

            logger.LogDebug("Broadcast status update: IsListening={IsListening}, Mode={Mode}", isListening, activationManager.CurrentMode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error broadcasting status update");
        }
    }

    private async void OnModeChanged(object? sender, ActivationMode mode)
    {
        try
        {
            // Broadcast status update to all connected controllers
            await ipcServer.BroadcastMessageAsync(new StatusUpdateMessage
            {
                IsListening = activationManager.IsListening,
                Mode = mode,
                StatusText = activationManager.IsListening ? "Listening" : "Idle",
                StatusColor = activationManager.IsListening ? "#22c55e" : "#808080"
            });

            logger.LogDebug("Broadcast status update: IsListening={IsListening}, Mode={Mode}", activationManager.IsListening, mode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error broadcasting status update");
        }
    }
}
