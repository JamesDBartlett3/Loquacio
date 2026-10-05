using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Loquacio.Services;

/// <summary>
/// Manages the daemon process lifecycle on Windows.
/// Detects running daemon via named pipe probe, starts it if needed, monitors health,
/// and restarts on crash. Uses Windows-specific paths and pipe detection.
/// </summary>
public sealed class WpfDaemonLifecycleService : IDisposable
{
    private readonly ILogger<WpfDaemonLifecycleService> _logger;
    private readonly string _daemonExecutablePath;
    private readonly TimeSpan _defaultHealthInterval = TimeSpan.FromSeconds(15);
    private readonly int _maxRestartAttempts = 3;
    private readonly TimeSpan _restartDelay = TimeSpan.FromSeconds(2);

    private Timer? _healthTimer;
    private WpfDaemonState _state = WpfDaemonState.Unknown;
    private int _consecutiveFailures;
    private bool _disposed;

    public WpfDaemonState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<WpfDaemonState>? StateChanged;
    public event EventHandler? HealthCheckFailed;

    /// <summary>True when this controller started the daemon process (and should stop it on exit).</summary>
    public bool StartedByController { get; private set; }

    public WpfDaemonLifecycleService(
        ILogger<WpfDaemonLifecycleService> logger,
        string? daemonExecutablePath = null)
    {
        _logger = logger;
        _daemonExecutablePath = daemonExecutablePath ?? FindDaemonExecutable();
    }

    /// <summary>
    /// Check if the daemon process is running by probing the named pipe.
    /// </summary>
    public async Task<bool> IsDaemonRunningAsync()
    {
        try
        {
            // Quick check: can we connect to the named pipe?
            using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                ".", "loquacio",
                System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.Asynchronous);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Start the daemon process if not already running.
    /// Returns true if daemon is (or was already) running.
    /// </summary>
    public async Task<bool> EnsureDaemonRunningAsync()
    {
        if (await IsDaemonRunningAsync())
        {
            State = WpfDaemonState.Running;
            return true;
        }

        return await StartDaemonProcessAsync();
    }

    /// <summary>
    /// Attempt to restart the daemon process.
    /// </summary>
    public async Task<bool> RestartDaemonAsync()
    {
        _logger.LogInformation("Restarting background service process");
        State = WpfDaemonState.Restarting;

        await KillDaemonProcessAsync();
        await Task.Delay(_restartDelay);

        return await StartDaemonProcessAsync();
    }

    /// <summary>
    /// Stop the daemon process this controller started (PID file based).
    /// </summary>
    public async Task StopDaemonAsync()
    {
        StopHealthMonitoring();
        _logger.LogInformation("Stopping background service (controller is exiting)");
        await KillDaemonProcessAsync();
        State = WpfDaemonState.Stopped;
    }

    public void StartHealthMonitoring(TimeSpan interval)
    {
        _healthTimer?.Dispose();
        _healthTimer = new Timer(
            async _ => await HealthCheckAsync(),
            null,
            interval,
            interval);
        _logger.LogDebug("Health monitoring started with {Interval}s interval", interval.TotalSeconds);
    }

    public void StartHealthMonitoring()
    {
        StartHealthMonitoring(_defaultHealthInterval);
    }

    public void StopHealthMonitoring()
    {
        _healthTimer?.Dispose();
        _healthTimer = null;
        _logger.LogDebug("Health monitoring stopped");
    }

    private async Task HealthCheckAsync()
    {
        if (_disposed) return;

        try
        {
            var running = await IsDaemonRunningAsync();
            if (running)
            {
                _consecutiveFailures = 0;
                if (State != WpfDaemonState.Running)
                    State = WpfDaemonState.Running;
            }
            else
            {
                _consecutiveFailures++;
                _logger.LogWarning("Daemon health check failed (attempt {Count})", _consecutiveFailures);

                State = WpfDaemonState.Unhealthy;
                HealthCheckFailed?.Invoke(this, EventArgs.Empty);

                if (_consecutiveFailures >= 2 && _consecutiveFailures <= _maxRestartAttempts)
                {
                    _logger.LogInformation("Auto-restarting daemon (attempt {Count})", _consecutiveFailures);
                    await StartDaemonProcessAsync();
                }
                else if (_consecutiveFailures > _maxRestartAttempts)
                {
                    State = WpfDaemonState.Stopped;
                    _logger.LogError("Daemon failed to restart after {Count} attempts. Giving up.", _maxRestartAttempts);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during health check");
        }
    }

    private async Task<bool> StartDaemonProcessAsync()
    {
        if (string.IsNullOrEmpty(_daemonExecutablePath) || !File.Exists(_daemonExecutablePath))
        {
            _logger.LogWarning("Daemon executable not found at {Path}. Cannot auto-start.", _daemonExecutablePath);
            State = WpfDaemonState.Stopped;
            return false;
        }

        try
        {
            State = WpfDaemonState.Starting;
            _logger.LogInformation("Starting daemon: {Path}", _daemonExecutablePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _daemonExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            var process = new Process { StartInfo = startInfo };
            process.Start();

            // Wire up output readers to prevent pipe buffer deadlock
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogDebug("[daemon] {Line}", e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogWarning("[daemon] {Line}", e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _logger.LogInformation("Background service process started (PID {Pid})", process.Id);

            // Wait for named pipe to become available
            var pipeReady = await WaitForPipeAsync(TimeSpan.FromSeconds(10));
            if (pipeReady)
            {
                StartedByController = true;
                State = WpfDaemonState.Running;
                _consecutiveFailures = 0;
                _logger.LogInformation("Background service is ready and accepting connections");
                return true;
            }
            else
            {
                _logger.LogError("Daemon started but pipe never became available");
                State = WpfDaemonState.Unhealthy;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start daemon process");
            State = WpfDaemonState.Stopped;
            return false;
        }
    }

    private async Task KillDaemonProcessAsync()
    {
        var pid = await TryReadDaemonPidAsync();
        if (pid is int && pid > 0)
        {
            try
            {
                var proc = Process.GetProcessById(pid.Value);
                proc.Kill();
                await proc.WaitForExitAsync();
                _logger.LogInformation("Killed daemon process (PID {Pid})", pid);
                return;
            }
            catch (ArgumentException)
            {
                _logger.LogDebug("Daemon process {Pid} already exited", pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill daemon process {Pid}", pid);
            }
        }

        // Fallback: the PID file may be unreadable (older daemons lock it) or
        // missing — find the daemon by its well-known process name instead.
        var daemons = Process.GetProcessesByName("loquacio-daemon");
        foreach (var proc in daemons)
        {
            try
            {
                proc.Kill();
                await proc.WaitForExitAsync();
                _logger.LogInformation("Killed daemon process by name (PID {Pid})", proc.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill daemon process (PID {Pid})", proc.Id);
            }
        }
    }

    private async Task<int?> TryReadDaemonPidAsync()
    {
        var pidPath = GetPidFilePath();
        if (!File.Exists(pidPath))
        {
            _logger.LogDebug("No PID file found at {Path}", pidPath);
            return null;
        }

        try
        {
            // The daemon may hold the PID file open — read with full sharing
            using var stream = new FileStream(pidPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            if (int.TryParse(text.Trim(), out var pid) && pid > 0) return pid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read daemon PID file at {Path}", pidPath);
        }
        return null;
    }

    private async Task<bool> WaitForPipeAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                    ".", "loquacio",
                    System.IO.Pipes.PipeDirection.InOut,
                    System.IO.Pipes.PipeOptions.Asynchronous);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await pipe.ConnectAsync(cts.Token);
                return true;
            }
            catch
            {
                // Pipe not ready yet
            }
            await Task.Delay(500);
        }

        return false;
    }

    private static string GetPidFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "loquacio", "daemon.pid");
    }

    private static string FindDaemonExecutable()
    {
        var baseDir = AppContext.BaseDirectory;

        var exeName = "loquacio-daemon.exe";

        var candidates = new[]
        {
            // Development: sibling build output
            Path.Combine(baseDir, "..", "..", "..", "Loquacio.Daemon", "bin", "Debug", "net10.0", exeName),
            Path.Combine(baseDir, "..", "..", "..", "Loquacio.Daemon", "bin", "Release", "net10.0", exeName),
            // Installed alongside
            Path.Combine(baseDir, exeName),
            // Common install locations
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loquacio", "daemon", exeName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Loquacio", "daemon", exeName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _healthTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Daemon process states (WPF-specific copy to avoid cross-project dependency issues).
/// </summary>
public enum WpfDaemonState
{
    Unknown,
    Running,
    Unhealthy,
    Stopped,
    Starting,
    Restarting
}
