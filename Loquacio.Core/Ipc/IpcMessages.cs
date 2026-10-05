using System.Text.Json.Serialization;
using Loquacio.Infrastructure;

namespace Loquacio.Ipc;

/// <summary>
/// Base type for all IPC messages between daemon and controllers.
/// Transport: newline-delimited JSON over Unix domain socket (Linux/macOS) or named pipe (Windows).
/// </summary>
public abstract class IpcMessage
{
    [JsonPropertyName("type")]
    public abstract string MessageType { get; }

    [JsonPropertyName("id")]
    public string? CorrelationId { get; set; }
}

// --- Daemon → Controller messages (status updates) ---

/// <summary>
/// Full status snapshot sent by daemon on subscription or state change.
/// </summary>
public class StatusUpdateMessage : IpcMessage
{
    public override string MessageType => "status";
    public bool IsListening { get; set; }
    public ActivationMode Mode { get; set; }
    public double AudioLevel { get; set; }
    public double PeakAudioLevel { get; set; }
    public string StatusText { get; set; } = "Idle";
    public string StatusColor { get; set; } = "#808080";

    /// <summary>Current dictation pipeline FSM stage (Idle/Recording/Transcribing/PostProcessing/Saving).</summary>
    public string PipelineState { get; set; } = nameof(Loquacio.Infrastructure.DictationState.Idle);

    /// <summary>Session id of the current/last pipeline run, if any.</summary>
    public string? SessionId { get; set; }
}

/// <summary>
/// Shared mapping from FSM state to human status text/color for StatusUpdateMessage.
/// </summary>
public static class PipelineStatusStyle
{
    public static (string Text, string Color) Describe(DictationState state, bool isListening)
    {
        if (!isListening) return ("Idle", "#808080");
        return state switch
        {
            DictationState.Idle => ("Listening", "#22c55e"),
            DictationState.Recording => ("Listening · Recording", "#22c55e"),
            DictationState.Transcribing => ("Listening · Transcribing", "#eab308"),
            DictationState.PostProcessing => ("Listening · Post-processing", "#eab308"),
            DictationState.Saving => ("Listening · Saving", "#3b82f6"),
            _ => ("Listening", "#22c55e"),
        };
    }
}

/// <summary>
/// New transcription result from the daemon.
/// </summary>
public class TranscriptionResultMessage : IpcMessage
{
    public override string MessageType => "transcription";
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public bool IsFinal { get; set; } = true;
}

/// <summary>
/// Settings were changed (by another controller or daemon).
/// </summary>
public class SettingsChangedMessage : IpcMessage
{
    public override string MessageType => "settings-changed";
    public string? ChangedSection { get; set; }
}

// --- Controller → Daemon messages (commands) ---

/// <summary>
/// Subscribe to status updates from the daemon.
/// </summary>
public class SubscribeMessage : IpcMessage
{
    public override string MessageType => "subscribe";
}

/// <summary>
/// Request full status snapshot.
/// </summary>
public class GetStatusMessage : IpcMessage
{
    public override string MessageType => "get-status";
}

/// <summary>
/// Request transcription history.
/// </summary>
public class GetHistoryMessage : IpcMessage
{
    public override string MessageType => "get-history";
    public int? Limit { get; set; }
    public int? Offset { get; set; }
}

/// <summary>
/// Update one or more settings sections.
/// </summary>
public class UpdateSettingsMessage : IpcMessage
{
    public override string MessageType => "update-settings";
    public Dictionary<string, object> Settings { get; set; } = new();
}

/// <summary>
/// Request the full settings snapshot from the daemon.
/// </summary>
public class GetSettingsMessage : IpcMessage
{
    public override string MessageType => "get-settings";
}

/// <summary>
/// Request the list of available audio capture devices from the daemon.
/// </summary>
public class GetDevicesMessage : IpcMessage
{
    public override string MessageType => "get-devices";
}

/// <summary>
/// Start listening.
/// </summary>
public class StartListeningMessage : IpcMessage
{
    public override string MessageType => "start-listening";
}

/// <summary>
/// Stop listening.
/// </summary>
public class StopListeningMessage : IpcMessage
{
    public override string MessageType => "stop-listening";
}

/// <summary>
/// Cancel the currently active dictation run (if any) and return the pipeline to Idle.
/// No-op success when no run is active. Aborts in-flight stage work (transcription,
/// post-processing, output) via the FSM's stage cancellation token.
/// </summary>
public class CancelDictationMessage : IpcMessage
{
    public override string MessageType => "cancel-dictation";
}

/// <summary>
/// Re-run post-processing and output for the last completed dictation run using
/// current settings (raw Whisper transcript is reused — no re-transcription).
/// Fails when there is no previous result or a run is currently active.
/// </summary>
public class ReprocessLastMessage : IpcMessage
{
    public override string MessageType => "reprocess-last";
}

/// <summary>
/// Set activation mode.
/// </summary>
public class SetModeMessage : IpcMessage
{
    public override string MessageType => "set-mode";
    public ActivationMode Mode { get; set; }
}

/// <summary>
/// Full settings snapshot sent by the daemon in response to get-settings.
/// </summary>
public class SettingsSnapshotMessage : IpcMessage
{
    public override string MessageType => "settings-snapshot";
    public Settings? Settings { get; set; }
}

/// <summary>
/// Available audio capture devices, sent by the daemon in response to get-devices.
/// </summary>
public class DevicesListMessage : IpcMessage
{
    public override string MessageType => "devices";
    public List<AudioDevice> Devices { get; set; } = [];
}

/// <summary>
/// Acknowledgement response from daemon.
/// </summary>
public class AckMessage : IpcMessage
{
    public override string MessageType => "ack";
    public bool Success { get; set; }
    public string? Error { get; set; }
}
