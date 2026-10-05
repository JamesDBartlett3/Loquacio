using Loquacio.Ipc;

namespace Loquacio.Avalonia.Services;

/// <summary>
/// Manages the daemon process lifecycle: detection, start, health monitoring, restart.
/// </summary>
public interface IDaemonLifecycleService : IDisposable
{
    /// <summary>
    /// Current daemon process state.
    /// </summary>
    DaemonState State { get; }

    /// <summary>
    /// Fired when the daemon state changes.
    /// </summary>
    event EventHandler<DaemonState>? StateChanged;

    /// <summary>
    /// Fired when a health check detects the daemon is down.
    /// </summary>
    event EventHandler? HealthCheckFailed;

    /// <summary>
    /// Check if the daemon process is running (PID file check + socket probe).
    /// </summary>
    Task<bool> IsDaemonRunningAsync();

    /// <summary>
    /// Start the daemon process if not already running.
    /// Returns true if daemon is (or was already) running.
    /// </summary>
    Task<bool> EnsureDaemonRunningAsync();

    /// <summary>
    /// Start periodic health monitoring with default interval.
    /// </summary>
    void StartHealthMonitoring();

    /// <summary>
    /// Start periodic health monitoring with custom interval.
    /// </summary>
    void StartHealthMonitoring(TimeSpan interval);

    /// <summary>
    /// Stop health monitoring.
    /// </summary>
    void StopHealthMonitoring();

    /// <summary>
    /// Attempt to restart the daemon process.
    /// </summary>
    Task<bool> RestartDaemonAsync();
}

/// <summary>
/// Daemon process states.
/// </summary>
public enum DaemonState
{
    /// <summary>Daemon has not been probed yet.</summary>
    Unknown,
    /// <summary>Daemon process is running and IPC socket is accepting connections.</summary>
    Running,
    /// <summary>Daemon process exists but IPC socket is not responding.</summary>
    Unhealthy,
    /// <summary>Daemon process is not running.</summary>
    Stopped,
    /// <summary>Daemon is being started.</summary>
    Starting,
    /// <summary>Daemon is being restarted after crash.</summary>
    Restarting
}
