using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Loquacio.Services;

/// <summary>
/// Manages the engine process lifecycle on Windows.
/// Detects running engine via named pipe probe, starts it if needed, monitors health,
/// and restarts on crash. Uses Windows-specific paths and pipe detection.
/// </summary>
public sealed class WpfEngineLifecycleService : IDisposable
{
    private readonly ILogger<WpfEngineLifecycleService> _logger;
    private readonly string _engineExecutablePath;
    private readonly TimeSpan _defaultHealthInterval = TimeSpan.FromSeconds(15);
    private readonly int _maxRestartAttempts = 3;
    private readonly TimeSpan _restartDelay = TimeSpan.FromSeconds(2);

    private Timer? _healthTimer;
    private WpfEngineState _state = WpfEngineState.Unknown;
    private int _consecutiveFailures;
    private bool _disposed;

    public WpfEngineState State
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

    public event EventHandler<WpfEngineState>? StateChanged;
    public event EventHandler? HealthCheckFailed;

    /// <summary>True when this controller started the engine process (and should stop it on exit).</summary>
    public bool StartedByController { get; private set; }

    public WpfEngineLifecycleService(
        ILogger<WpfEngineLifecycleService> logger,
        string? engineExecutablePath = null)
    {
        _logger = logger;
        _engineExecutablePath = engineExecutablePath ?? FindEngineExecutable();
    }

    /// <summary>
    /// Check if the engine process is running by probing the named pipe.
    /// </summary>
    public async Task<bool> IsEngineRunningAsync()
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
    /// Start the engine process if not already running.
    /// Returns true if engine is (or was already) running.
    /// </summary>
    public async Task<bool> EnsureEngineRunningAsync()
    {
        if (await IsEngineRunningAsync())
        {
            State = WpfEngineState.Running;
            return true;
        }

        return await StartEngineProcessAsync();
    }

    /// <summary>
    /// Attempt to restart the engine process.
    /// </summary>
    public async Task<bool> RestartEngineAsync()
    {
        _logger.LogInformation("Restarting background service process");
        State = WpfEngineState.Restarting;

        await KillEngineProcessAsync();
        await Task.Delay(_restartDelay);

        return await StartEngineProcessAsync();
    }

    /// <summary>
    /// Stop the engine process this controller started (PID file based).
    /// </summary>
    public async Task StopEngineAsync()
    {
        StopHealthMonitoring();
        _logger.LogInformation("Stopping background service (controller is exiting)");
        await KillEngineProcessAsync();
        State = WpfEngineState.Stopped;
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
            var running = await IsEngineRunningAsync();
            if (running)
            {
                _consecutiveFailures = 0;
                if (State != WpfEngineState.Running)
                    State = WpfEngineState.Running;
            }
            else
            {
                _consecutiveFailures++;
                _logger.LogWarning("Engine health check failed (attempt {Count})", _consecutiveFailures);

                State = WpfEngineState.Unhealthy;
                HealthCheckFailed?.Invoke(this, EventArgs.Empty);

                if (_consecutiveFailures >= 2 && _consecutiveFailures <= _maxRestartAttempts)
                {
                    _logger.LogInformation("Auto-restarting engine (attempt {Count})", _consecutiveFailures);
                    await StartEngineProcessAsync();
                }
                else if (_consecutiveFailures > _maxRestartAttempts)
                {
                    State = WpfEngineState.Stopped;
                    _logger.LogError("Engine failed to restart after {Count} attempts. Giving up.", _maxRestartAttempts);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during health check");
        }
    }

    private async Task<bool> StartEngineProcessAsync()
    {
        if (string.IsNullOrEmpty(_engineExecutablePath) || !File.Exists(_engineExecutablePath))
        {
            _logger.LogWarning("Engine executable not found at {Path}. Cannot auto-start.", _engineExecutablePath);
            State = WpfEngineState.Stopped;
            return false;
        }

        try
        {
            State = WpfEngineState.Starting;
            _logger.LogInformation("Starting engine: {Path}", _engineExecutablePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _engineExecutablePath,
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
                    _logger.LogDebug("[engine] {Line}", e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogWarning("[engine] {Line}", e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _logger.LogInformation("Background service process started (PID {Pid})", process.Id);

            // Wait for named pipe to become available
            var pipeReady = await WaitForPipeAsync(TimeSpan.FromSeconds(10));
            if (pipeReady)
            {
                StartedByController = true;
                State = WpfEngineState.Running;
                _consecutiveFailures = 0;
                _logger.LogInformation("Background service is ready and accepting connections");
                return true;
            }
            else
            {
                _logger.LogError("Engine started but pipe never became available");
                State = WpfEngineState.Unhealthy;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start engine process");
            State = WpfEngineState.Stopped;
            return false;
        }
    }

    private async Task KillEngineProcessAsync()
    {
        var pid = await TryReadEnginePidAsync();
        if (pid is int && pid > 0)
        {
            try
            {
                var proc = Process.GetProcessById(pid.Value);
                proc.Kill();
                await proc.WaitForExitAsync();
                _logger.LogInformation("Killed engine process (PID {Pid})", pid);
                return;
            }
            catch (ArgumentException)
            {
                _logger.LogDebug("Engine process {Pid} already exited", pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill engine process {Pid}", pid);
            }
        }

        // Fallback: the PID file may be unreadable (older engines lock it) or
        // missing — find the engine by its well-known process name instead.
        var engines = Process.GetProcessesByName("loquacio-engine");
        foreach (var proc in engines)
        {
            try
            {
                proc.Kill();
                await proc.WaitForExitAsync();
                _logger.LogInformation("Killed engine process by name (PID {Pid})", proc.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill engine process (PID {Pid})", proc.Id);
            }
        }
    }

    private async Task<int?> TryReadEnginePidAsync()
    {
        var pidPath = GetPidFilePath();
        if (!File.Exists(pidPath))
        {
            _logger.LogDebug("No PID file found at {Path}", pidPath);
            return null;
        }

        try
        {
            // The engine may hold the PID file open — read with full sharing
            using var stream = new FileStream(pidPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            if (int.TryParse(text.Trim(), out var pid) && pid > 0) return pid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read engine PID file at {Path}", pidPath);
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
        return Path.Combine(appData, "loquacio", "engine.pid");
    }

    private static string FindEngineExecutable()
    {
        var baseDir = AppContext.BaseDirectory;

        var exeName = "loquacio-engine.exe";

        var candidates = new[]
        {
            // Development: sibling build output
            Path.Combine(baseDir, "..", "..", "..", "Loquacio.Engine", "bin", "Debug", "net10.0", exeName),
            Path.Combine(baseDir, "..", "..", "..", "Loquacio.Engine", "bin", "Release", "net10.0", exeName),
            // Installed alongside
            Path.Combine(baseDir, exeName),
            // Common install locations
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loquacio", "engine", exeName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Loquacio", "engine", exeName),
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
/// Engine process states (WPF-specific copy to avoid cross-project dependency issues).
/// </summary>
public enum WpfEngineState
{
    Unknown,
    Running,
    Unhealthy,
    Stopped,
    Starting,
    Restarting
}
