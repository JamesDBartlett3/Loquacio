using Microsoft.Extensions.Logging;
using Loquacio.Infrastructure;

namespace Loquacio.Services;

/// <summary>
/// Service that coordinates the full pipeline:
/// Audio Capture → Whisper → LLM Post-Processing → Vocabulary Context → Clipboard Output
/// </summary>
public class BackgroundTranscriptionService : IBackgroundTranscriptionService, IDisposable
{
    private readonly IAudioCaptureService _audioCapture;
    private readonly IWhisperProcessorService _whisperProcessor;
    private readonly ISettingsService _settingsService;
    private readonly ILLMPostProcessorService _llmPostProcessor;
    private readonly IVocabularyService _vocabularyService;
    private readonly IClipboardService _clipboardService;
    private readonly ILogger<BackgroundTranscriptionService> _logger;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _linkedCts;
    private Task? _processingTask;
    private bool _disposed;
    private bool _isRunning;
    private string? _lastLoadedModelPath;
    private string? _lastLoadedLanguage;
    private TranscriptionResult? _lastResult;

    /// <inheritdoc />
    public TranscriptionResult? LastResult => _lastResult;

    public event EventHandler<TranscriptionResult>? OnTranscriptionCompleted;
    public event EventHandler<Exception>? OnError;
    public event EventHandler<double>? OnAudioLevel;

    /// <summary>
    /// Per-segment dictation FSM. Drives Transcribing → PostProcessing → Saving
    /// for each dequeued segment, with per-stage safety timeouts that force a
    /// run back to Idle so a hung Whisper/LLM/output call can never wedge the
    /// pipeline. Exposed for controllers that want to surface pipeline state.
    /// </summary>
    public DictationStateMachine Fsm { get; } = new();

    public DictationState PipelineState => Fsm.State;

    public Guid? PipelineSessionId => Fsm.SessionId;

    public event Action<DictationStateChangedEventArgs>? PipelineStateChanged
    {
        add => Fsm.StateChanged += value;
        remove => Fsm.StateChanged -= value;
    }

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Cancel the active dictation run from any state (idempotent). Returns false
    /// when the FSM is already Idle. In-flight stage work observes cancellation
    /// via the FSM stage token; the processing loop's stale-session guards
    /// tolerate the subsequent abort.
    /// </summary>
    public bool CancelActiveRun()
    {
        var sessionId = Fsm.SessionId;
        if (sessionId is null || Fsm.State == DictationState.Idle)
            return false;

        Fsm.Cancel(sessionId.Value);
        return true;
    }

    public BackgroundTranscriptionService(
        IAudioCaptureService audioCapture,
        IWhisperProcessorService whisperProcessor,
        ISettingsService settingsService,
        ILLMPostProcessorService llmPostProcessor,
        IVocabularyService vocabularyService,
        IClipboardService clipboardService,
        ILogger<BackgroundTranscriptionService> logger)
    {
        _audioCapture = audioCapture;
        _whisperProcessor = whisperProcessor;
        _settingsService = settingsService;
        _llmPostProcessor = llmPostProcessor;
        _vocabularyService = vocabularyService;
        _clipboardService = clipboardService;
        _logger = logger;

        // Subscribe to audio capture events
        _audioCapture.OnSegmentCaptured += OnSegmentCaptured;
        _audioCapture.OnError += OnAudioError;
        _audioCapture.OnAudioLevel += OnCaptureAudioLevel;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("Background transcription service is already running");
            return;
        }

        _logger.LogInformation("Starting background transcription service");

        // Create a fresh CTS for each start cycle (previous StopAsync cancels the old one)
        _cts = new CancellationTokenSource();
        _linkedCts?.Dispose();
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var effectiveCt = _linkedCts.Token;

        var settings = await _settingsService.GetSettingsAsync(ct);

        // Refresh LLM post-processor state
        if (_llmPostProcessor is LLMPostProcessorService concreteLlm)
        {
            await concreteLlm.RefreshStateAsync(ct);
        }

        // Pre-load vocabulary
        await _vocabularyService.GetWordsAsync(ct);

        // Set output mode from settings
        _clipboardService.OutputMode = settings.Output.Mode;

        // Load Whisper model if not already loaded, or if language/path changed since last load
        var needsLoad = !_whisperProcessor.IsModelLoaded
            || _lastLoadedModelPath != settings.Whisper.ModelPath
            || _lastLoadedLanguage != settings.Whisper.Language;

        if (needsLoad && !string.IsNullOrWhiteSpace(settings.Whisper.ModelPath))
        {
            try
            {
                _whisperProcessor.LoadModel(settings.Whisper.ModelPath, settings.Whisper.Language);
                _lastLoadedModelPath = settings.Whisper.ModelPath;
                _lastLoadedLanguage = settings.Whisper.Language;
                _logger.LogInformation("Whisper model loaded: {Path} (language: {Language})", settings.Whisper.ModelPath, settings.Whisper.Language);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Whisper model from {Path}", settings.Whisper.ModelPath);
                throw;
            }
        }
        else if (!_whisperProcessor.IsModelLoaded)
        {
            // Make "no model configured" loud and actionable instead of silently
            // producing nothing: refuse to start with a clear error.
            throw new InvalidOperationException(
                "No Whisper model configured. Open the controller's Whisper tab and download a model first.");
        }

        // Get default audio device
        var devices = _audioCapture.GetAvailableDevices();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No audio capture devices available");
        }

        var device = devices.FirstOrDefault(d => d.Id == settings.Audio.DeviceId) ?? devices[0];

        // Start audio capture
        _audioCapture.StartCapture(device, settings.Audio.SilenceThresholdMs, settings.Audio.SilenceThresholdDb,
            settings.Audio.Gain, settings.Audio.CompressorEnabled, settings.Audio.CompressorThresholdDb,
            settings.Audio.CompressorRatio);

        // Start background processing task
        _processingTask = Task.Run(() => ProcessingLoopAsync(effectiveCt), effectiveCt);

        _isRunning = true;
        _logger.LogInformation("Background transcription service started with device {Device}", device.FriendlyName);
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _logger.LogInformation("Stopping background transcription service");

        _isRunning = false;
        _audioCapture.StopCapture();

        // Cancel the processing loop
        _cts?.Cancel();

        // Abort any in-flight dictation stage run: stage work observes the FSM
        // stage token, not the pipeline token, so without this a gated/hung
        // stage would leave StopAsync awaiting _processingTask forever.
        TryCancelActiveRun("service stop");

        try
        {
            if (_processingTask is not null)
            {
                await _processingTask;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Dispose linked CTS to prevent resource leak across start/stop cycles
        _linkedCts?.Dispose();
        _linkedCts = null;

        _logger.LogInformation("Background transcription service stopped");
    }

    private async Task ProcessingLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Starting transcription processing loop");

        try
        {
            await foreach (var segment in AudioChannel.ReadAllAsync(ct))
            {
                if (!_isRunning)
                {
                    continue;
                }

                _logger.LogDebug("Processing audio segment: {Duration}s", segment.Duration.TotalSeconds);

                try
                {
                    // Settings freeze per run (WhisperVoiceInput inspiration concept #3):
                    // snapshot settings once at run start; all stages of this run use the
                    // frozen snapshot. Concurrent settings edits apply at the next run
                    // start, never mid-run — eliminates a class of mid-pipeline drift bugs
                    // (e.g. output mode changing between the transcript and output stages).
                    var runSettings = (await _settingsService.GetSettingsAsync(ct)).Snapshot();
                    var settingsPipeline = runSettings.Pipeline;
                    _clipboardService.OutputMode = runSettings.Output.Mode;
                    var sessionId = StartOrResetFsmRun();
                    var rawTranscription = await ProcessSegmentWithFsmAsync(segment, settingsPipeline, ct);

                    if (string.IsNullOrWhiteSpace(rawTranscription))
                    {
                        // Silent/no-speech segment: end the run cleanly instead of
                        // leaving the FSM in Transcribing until the next segment.
                        TryCancelActiveRun("empty transcription");
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(rawTranscription))
                    {
                        var result = await RunPostProcessAndOutputAsync(sessionId, runSettings, rawTranscription,
                            segment.Timestamp, segment.Duration, ct);

                        OnTranscriptionCompleted?.Invoke(this, result);
                        _logger.LogInformation("Transcription completed (post-processed: {PostProcessed}): {Text}",
                            result.IsPostProcessed, result.Text);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing audio segment");
                    OnError?.Invoke(this, ex);
                    // Never leave the FSM wedged in a mid-pipeline state after a
                    // failed segment: cancel the run back to Idle (idempotent).
                    TryCancelActiveRun("segment processing error");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Transcription processing loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in transcription processing loop");
            OnError?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// Begin a new FSM run for a dequeued segment. The audio was already
    /// captured by the time we dequeue it, so the run enters at Transcribing
    /// (via a nominal Recording pass-through). Any previous wedged run is
    /// force-cancelled first — the pipeline is strictly sequential, so a
    /// non-Idle machine here means an earlier segment escaped cleanup.
    /// </summary>
    private Guid StartOrResetFsmRun()
    {
        if (Fsm.State != DictationState.Idle)
        {
            _logger.LogWarning("FSM was left in {State} before a new segment; cancelling stale run", Fsm.State);
            TryCancelActiveRun("stale run before new segment");
        }

        return Fsm.StartSession();
    }

    private void TryCancelActiveRun(string reason)
    {
        var sessionId = Fsm.SessionId;
        if (sessionId is { } id && Fsm.State != DictationState.Idle)
        {
            try { Fsm.Cancel(id); }
            catch (InvalidOperationException) { /* raced to Idle — fine */ }
        }
    }

    /// <summary>
    /// Run the Whisper stage under FSM supervision: Recording (nominal) →
    /// Transcribing with the configured stage timeout.
    /// </summary>
    private Task<string> ProcessSegmentWithFsmAsync(AudioSegment segment, PipelineSettings pipeline, CancellationToken ct)
    {
        // The segment was fully captured upstream; Recording is entered and
        // left immediately as a pass-through so the transition table holds.
        var sessionId = Fsm.SessionId!.Value;
        Fsm.TransitionTo(sessionId, DictationState.Recording);
        return RunFsmStageAsync(sessionId, DictationState.Transcribing,
            pipeline.TranscribeTimeoutSeconds, pipeline.EnableStageTimeouts,
            stageCt => _whisperProcessor.ProcessAsync(segment, stageCt));
    }

    /// <summary>
    /// Transition to <paramref name="stage"/>, run <paramref name="work"/> with
    /// a stage token (cancelled on pipeline stop or stage timeout), and return
    /// its result. The FSM's stage timeout force-cancels the run back to Idle
    /// when it expires; the stage token then aborts the hung work.
    /// </summary>
    private async Task<T> RunFsmStageAsync<T>(Guid sessionId, DictationState stage,
        int timeoutSeconds, bool timeoutsEnabled, Func<CancellationToken, Task<T>> work)
    {
        TimeSpan? timeout = timeoutsEnabled && timeoutSeconds > 0
            ? TimeSpan.FromSeconds(timeoutSeconds)
            : null;
        var stageToken = Fsm.TransitionTo(sessionId, stage, timeout);

        // Link the pipeline token so service shutdown also aborts stage work.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stageToken);
        try
        {
            return await work(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            _logger.LogWarning("Dictation stage {Stage} was cancelled (timeout or pipeline stop)", stage);
            throw;
        }
    }

    /// <summary>Non-generic variant for stages that produce no value.</summary>
    private async Task RunFsmStageAsync(Guid sessionId, DictationState stage,
        int timeoutSeconds, bool timeoutsEnabled, Func<CancellationToken, Task> work)
    {
        TimeSpan? timeout = timeoutsEnabled && timeoutSeconds > 0
            ? TimeSpan.FromSeconds(timeoutSeconds)
            : null;
        var stageToken = Fsm.TransitionTo(sessionId, stage, timeout);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stageToken);
        try
        {
            await work(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            _logger.LogWarning("Dictation stage {Stage} was cancelled (timeout or pipeline stop)", stage);
            throw;
        }
    }

    /// <summary>
    /// Post-process the raw transcript (LLM + filler removal), produce the
    /// <see cref="TranscriptionResult"/>, deliver the output (inject/type/clipboard
    /// per the frozen run settings), and complete the run (Saving → Idle).
    /// Shared by the live pipeline and ReprocessLastAsync.
    /// </summary>
    private async Task<TranscriptionResult> RunPostProcessAndOutputAsync(
        Guid sessionId, Settings runSettings, string rawTranscription,
        DateTime timestamp, TimeSpan duration, CancellationToken ct)
    {
        var settingsPipeline = runSettings.Pipeline;

        // Get vocabulary context for LLM
        var vocabulary = await _vocabularyService.GetWordsAsync(ct);

        // Post-process through LLM (if available) + filler removal
        var processedText = await RunFsmStageAsync(sessionId, DictationState.PostProcessing,
            settingsPipeline.PostProcessTimeoutSeconds, settingsPipeline.EnableStageTimeouts,
            stageCt => _llmPostProcessor.ProcessAsync(rawTranscription, vocabulary, stageCt));

        var wasPostProcessed = processedText != rawTranscription;

        var result = new TranscriptionResult
        {
            Text = processedText,
            RawText = wasPostProcessed ? rawTranscription : null,
            Timestamp = timestamp,
            Duration = duration,
            Language = null, // Will be detected by Whisper
            IsPostProcessed = wasPostProcessed
        };

        // Output — inject into the focused window ("inject"/"type") or copy to
        // the clipboard, per the frozen run's Output settings.
        await RunFsmStageAsync(sessionId, DictationState.Saving,
            settingsPipeline.OutputTimeoutSeconds, settingsPipeline.EnableStageTimeouts,
            async stageCt =>
            {
                if (runSettings.Output.Mode is "inject" or "type")
                    await _clipboardService.TypeTextAsync(processedText, ct: stageCt);
                else
                    await _clipboardService.CopyToClipboardAsync(processedText, stageCt);
            });
        // Complete the run: Saving → Idle
        Fsm.TransitionTo(sessionId, DictationState.Idle);

        _lastResult = result;
        return result;
    }

    /// <summary>
    /// Re-run post-processing + output for the last completed dictation run with
    /// current settings. The raw Whisper transcript is reused (no re-transcription),
    /// so this is the "same audio, new settings" path: flip LLM on, change output
    /// mode, add vocabulary words — then reprocess.
    /// </summary>
    public async Task<TranscriptionResult?> ReprocessLastAsync(CancellationToken ct = default)
    {
        var last = _lastResult;
        if (last is null)
            return null;

        if (Fsm.State != DictationState.Idle)
            throw new InvalidOperationException(
                $"Cannot reprocess while a dictation run is active (state: {Fsm.State}). Wait for it to finish or cancel it first.");

        // Raw source: the un-post-processed transcript when we have one; the
        // final text otherwise (that run skipped post-processing).
        var raw = last.RawText ?? last.Text;

        var runSettings = (await _settingsService.GetSettingsAsync(ct)).Snapshot();
        _clipboardService.OutputMode = runSettings.Output.Mode;
        var sessionId = StartOrResetFsmRun();

        try
        {
            // No re-transcription (raw transcript reused) — but the FSM transition
            // table requires the Recording and Transcribing pass-throughs before
            // PostProcessing, so enter and leave them immediately.
            Fsm.TransitionTo(sessionId, DictationState.Recording);
            Fsm.TransitionTo(sessionId, DictationState.Transcribing);

            var result = await RunPostProcessAndOutputAsync(sessionId, runSettings, raw,
                last.Timestamp, last.Duration, ct);

            OnTranscriptionCompleted?.Invoke(this, result);
            _logger.LogInformation("Reprocessed last dictation (post-processed: {PostProcessed}): {Text}",
                result.IsPostProcessed, result.Text);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reprocessing last dictation");
            TryCancelActiveRun("reprocess error");
            OnError?.Invoke(this, ex);
            throw;
        }
    }

    private void OnSegmentCaptured(object? sender, AudioSegment segment)
    {
        // Segment is automatically written to AudioChannel by AudioCaptureService
        // This event is for notification purposes
        _logger.LogDebug("Audio segment captured: {Duration}s", segment.Duration.TotalSeconds);
    }

    private void OnAudioError(object? sender, Exception ex)
    {
        _logger.LogError(ex, "Audio capture error");
        OnError?.Invoke(this, ex);
    }

    private void OnCaptureAudioLevel(object? sender, double level)
    {
        OnAudioLevel?.Invoke(this, level);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAsync().GetAwaiter().GetResult();

        _audioCapture.OnSegmentCaptured -= OnSegmentCaptured;
        _audioCapture.OnError -= OnAudioError;
        _audioCapture.OnAudioLevel -= OnCaptureAudioLevel;

        _linkedCts?.Dispose();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}