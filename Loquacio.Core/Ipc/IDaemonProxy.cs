namespace Loquacio.Ipc;

/// <summary>
/// Interface for controllers to communicate with the daemon.
/// The daemon implements this; controllers call it.
/// </summary>
public interface IDaemonProxy : IDisposable
{
    /// <summary>
    /// Connect to the daemon. Unix domain socket on Linux/macOS, named pipe on Windows.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Disconnect from the daemon.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Whether currently connected to the daemon.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Send a command to the daemon and wait for acknowledgement.
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
