using Loquacio.Infrastructure;
using Loquacio.Models;

namespace Loquacio.Services;

/// <summary>
/// Service that coordinates audio capture and Whisper processing in the background
/// </summary>
public interface IBackgroundTranscriptionService
{
    /// <summary>
    /// Event fired when a transcription is completed
    /// </summary>
    event EventHandler<TranscriptionResult>? OnTranscriptionCompleted;

    /// <summary>
    /// Event fired when an error occurs
    /// </summary>
    event EventHandler<Exception>? OnError;

    /// <summary>
    /// Event fired with the live audio input level (0..1) while capturing —
    /// used by controllers for VU meter feedback.
    /// </summary>
    event EventHandler<double>? OnAudioLevel;

    /// <summary>
    /// Current dictation pipeline FSM stage (mirrors BackgroundTranscriptionService.Fsm.State).
    /// </summary>
    DictationState PipelineState { get; }

    /// <summary>
    /// Session id of the current/last pipeline run, or null if never started.
    /// </summary>
    Guid? PipelineSessionId { get; }

    /// <summary>
    /// Raised after every pipeline FSM state change. Subscribers must not call
    /// back into the FSM synchronously (raised under its lock).
    /// </summary>
    event Action<DictationStateChangedEventArgs>? PipelineStateChanged;

    /// <summary>
    /// Start the background transcription service
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stop the background transcription service
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Whether the service is running
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Cancel the currently active dictation run from any pipeline state and
    /// return the FSM to Idle. Idempotent no-op (returns false) when no run is
    /// active. Aborts in-flight stage work via the FSM stage cancellation token.
    /// </summary>
    /// <returns>true if an active run was cancelled; false when already Idle.</returns>
    bool CancelActiveRun();

    /// <summary>
    /// The last completed transcription result (raw + processed text), or null
    /// if no run has completed since the service was created.
    /// </summary>
    TranscriptionResult? LastResult { get; }

    /// <summary>
    /// Re-run post-processing and output for the last completed dictation run
    /// using the CURRENT settings (LLM on/off, vocabulary, output mode). The raw
    /// Whisper transcript is reused — no re-transcription. Fires
    /// <see cref="OnTranscriptionCompleted"/> and updates <see cref="LastResult"/>.
    /// </summary>
    /// <returns>The reprocessed result, or null when there is no previous result.</returns>
    /// <exception cref="InvalidOperationException">A dictation run is currently active.</exception>
    Task<TranscriptionResult?> ReprocessLastAsync(CancellationToken ct = default);
}