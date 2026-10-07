using Loquacio.Ipc;

namespace Loquacio.Avalonia.Services;

/// <summary>
/// Manages the engine process lifecycle: detection, start, health monitoring, restart.
/// </summary>
public interface IEngineLifecycleService : IDisposable
{
    /// <summary>
    /// Current engine process state.
    /// </summary>
    EngineState State { get; }

    /// <summary>
    /// Fired when the engine state changes.
    /// </summary>
    event EventHandler<EngineState>? StateChanged;

    /// <summary>
    /// Fired when a health check detects the engine is down.
    /// </summary>
    event EventHandler? HealthCheckFailed;

    /// <summary>
    /// Check if the engine process is running (PID file check + socket probe).
    /// </summary>
    Task<bool> IsEngineRunningAsync();

    /// <summary>
    /// Start the engine process if not already running.
    /// Returns true if engine is (or was already) running.
    /// </summary>
    Task<bool> EnsureEngineRunningAsync();

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
    /// Attempt to restart the engine process.
    /// </summary>
    Task<bool> RestartEngineAsync();
}

/// <summary>
/// Engine process states.
/// </summary>
public enum EngineState
{
    /// <summary>Engine has not been probed yet.</summary>
    Unknown,
    /// <summary>Engine process is running and IPC socket is accepting connections.</summary>
    Running,
    /// <summary>Engine process exists but IPC socket is not responding.</summary>
    Unhealthy,
    /// <summary>Engine process is not running.</summary>
    Stopped,
    /// <summary>Engine is being started.</summary>
    Starting,
    /// <summary>Engine is being restarted after crash.</summary>
    Restarting
}
