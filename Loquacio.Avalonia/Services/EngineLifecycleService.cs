using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Loquacio.Ipc;

namespace Loquacio.Avalonia.Services;

/// <summary>
/// Manages the engine process lifecycle from the Avalonia controller.
/// Detects running engine, starts it if needed, monitors health, and restarts on crash.
/// </summary>
public sealed class EngineLifecycleService : IEngineLifecycleService
{
    private readonly ILogger<EngineLifecycleService> _logger;
    private readonly string _engineExecutablePath;
    private readonly string _socketPath;
    private readonly string _pidFilePath;
    private readonly TimeSpan _defaultHealthInterval = TimeSpan.FromSeconds(15);
    private readonly int _maxRestartAttempts = 3;
    private readonly TimeSpan _restartDelay = TimeSpan.FromSeconds(2);

    private Timer? _healthTimer;
    private EngineState _state = EngineState.Unknown;
    private int _consecutiveFailures;
    private bool _disposed;

    public EngineState State
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

    public event EventHandler<EngineState>? StateChanged;

    /// <summary>
    /// Fired when a health check detects the engine is down.
    /// The Avalonia controller should update UI accordingly.
    /// </summary>
    public event EventHandler? HealthCheckFailed;

    public EngineLifecycleService(
        ILogger<EngineLifecycleService> logger,
        string? engineExecutablePath = null,
        string? socketPath = null,
        string? pidFilePath = null)
    {
        _logger = logger;
        _engineExecutablePath = engineExecutablePath ?? FindEngineExecutable();
        _socketPath = socketPath ?? GetDefaultSocketPath();
        _pidFilePath = pidFilePath ?? GetDefaultPidFilePath();
    }

    public async Task<bool> IsEngineRunningAsync()
    {
        // 1. Check if socket file exists
        var socketPath = _socketPath;
        if (!File.Exists(socketPath))
            return false;

        // 2. Probe the socket with a quick connect attempt
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnsureEngineRunningAsync()
    {
        if (await IsEngineRunningAsync())
        {
            State = EngineState.Running;
            return true;
        }

        return await StartEngineProcessAsync();
    }

    public async Task<bool> RestartEngineAsync()
    {
        _logger.LogInformation("Restarting engine process");
        State = EngineState.Restarting;

        // Kill existing process if any (via PID file)
        await KillEngineProcessAsync();

        // Brief delay for cleanup
        await Task.Delay(_restartDelay);

        return await StartEngineProcessAsync();
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
                if (State != EngineState.Running)
                    State = EngineState.Running;
            }
            else
            {
                _consecutiveFailures++;
                _logger.LogWarning("Engine health check failed (attempt {Count})", _consecutiveFailures);

                State = EngineState.Unhealthy;
                HealthCheckFailed?.Invoke(this, EventArgs.Empty);

                // Auto-restart after consecutive failures
                if (_consecutiveFailures >= 2 && _consecutiveFailures <= _maxRestartAttempts)
                {
                    _logger.LogInformation("Auto-restarting engine (attempt {Count})", _consecutiveFailures);
                    await StartEngineProcessAsync();
                }
                else if (_consecutiveFailures > _maxRestartAttempts)
                {
                    State = EngineState.Stopped;
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
            State = EngineState.Stopped;
            return false;
        }

        try
        {
            State = EngineState.Starting;
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

            // Wire up output readers to prevent pipe buffer deadlock.
            // If stdout/stderr are redirected but never read, the OS pipe buffer
            // fills up and the engine blocks on its next write — appearing to hang.
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

            _logger.LogInformation("Engine process started (PID {Pid})", process.Id);

            // Wait for socket to become available
            var socketReady = await WaitForSocketAsync(TimeSpan.FromSeconds(10));
            if (socketReady)
            {
                State = EngineState.Running;
                _consecutiveFailures = 0;
                _logger.LogInformation("Engine is ready and accepting connections");
                return true;
            }
            else
            {
                _logger.LogError("Engine started but socket never became available");
                State = EngineState.Unhealthy;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start engine process");
            State = EngineState.Stopped;
            return false;
        }
    }

    private async Task KillEngineProcessAsync()
    {
        var pidPath = _pidFilePath;
        if (!File.Exists(pidPath))
        {
            _logger.LogDebug("No PID file found at {Path}", pidPath);
            return;
        }

        try
        {
            var pidText = await File.ReadAllTextAsync(pidPath);
            if (int.TryParse(pidText.Trim(), out var pid) && pid > 0)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    proc.Kill();
                    await proc.WaitForExitAsync();
                    _logger.LogInformation("Killed engine process (PID {Pid})", pid);
                }
                catch (ArgumentException)
                {
                    _logger.LogDebug("Engine process {Pid} already exited", pid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill engine process via PID file");
        }
    }

    private async Task<bool> WaitForSocketAsync(TimeSpan timeout)
    {
        var socketPath = _socketPath;
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(socketPath))
            {
                try
                {
                    using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cts.Token);
                    return true;
                }
                catch
                {
                    // Socket exists but not ready yet
                }
            }
            await Task.Delay(500);
        }

        return false;
    }

    private static string GetDefaultSocketPath()
    {
        var runDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runDir))
            return Path.Combine(runDir, "loquacio.sock");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "loquacio", "engine.sock");
    }

    private static string GetDefaultPidFilePath()
    {
        var runDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runDir))
            return Path.Combine(runDir, "loquacio.pid");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "loquacio", "engine.pid");
    }

    private static string FindEngineExecutable()
    {
        // Look relative to the Avalonia app directory (sibling project in build output)
        var baseDir = AppContext.BaseDirectory;

        // Development:../../loquacio-engine/bin/Debug/net10.0/
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "Loquacio.Engine", "bin", "Debug", "net10.0", "loquacio-engine"),
            Path.Combine(baseDir, "..", "..", "..", "Loquacio.Engine", "bin", "Release", "net10.0", "loquacio-engine"),
            // Installed alongside
            Path.Combine(baseDir, "loquacio-engine"),
            // System install
            "/usr/local/bin/loquacio-engine",
            "/usr/bin/loquacio-engine",
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
