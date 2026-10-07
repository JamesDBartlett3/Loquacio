namespace Loquacio.Ipc;

/// <summary>
/// Interface for controllers to communicate with the engine.
/// The engine implements this; controllers call it.
/// </summary>
public interface IEngineProxy : IDisposable
{
    /// <summary>
    /// Connect to the engine. Unix domain socket on Linux/macOS, named pipe on Windows.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Disconnect from the engine.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Whether currently connected to the engine.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Send a command to the engine and wait for acknowledgement.
    /// </summary>
    Task<AckMessage> SendCommandAsync(IpcMessage command, CancellationToken ct = default);

    /// <summary>
    /// Cancel the currently active dictation run (no-op success when idle).
    /// </summary>
    Task<AckMessage> CancelDictationAsync(CancellationToken ct = default);

    /// <summary>
    /// Subscribe to status updates. The callback fires on each status/transcription message.
    /// </summary>
    void SubscribeToUpdates(Action<IpcMessage> onMessage);

    /// <summary>
    /// Unsubscribe from status updates.
    /// </summary>
    void UnsubscribeFromUpdates();

    /// <summary>
    /// Fired when the connection is lost.
    /// </summary>
    event EventHandler? Disconnected;
}
