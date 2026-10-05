using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using WhisperDictation.Ipc;

namespace WhisperDictation.Avalonia.Services;

/// <summary>
/// Manages the daemon process lifecycle from the Avalonia controller.
/// Detects running daemon, starts it if needed, monitors health, and restarts on crash.
/// </summary>
public sealed class DaemonLifecycleService : IDaemonLifecycleService
{
    private readonly ILogger<DaemonLifecycleService> _logger;
    private readonly string _daemonExecutablePath;
    private readonly string _socketPath;
    private readonly string _pidFilePath;
    private readonly TimeSpan _defaultHealthInterval = TimeSpan.FromSeconds(15);
    private readonly int _maxRestartAttempts = 3;
    private readonly TimeSpan _restartDelay = TimeSpan.FromSeconds(2);

    private Timer? _healthTimer;
    private DaemonState _state = DaemonState.Unknown;
    private int _consecutiveFailures;
    private bool _disposed;

    public DaemonState State
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

    public event EventHandler<DaemonState>? StateChanged;

    /// <summary>
    /// Fired when a health check detects the daemon is down.
    /// The Avalonia controller should update UI accordingly.
    /// </summary>
    public event EventHandler? HealthCheckFailed;

    public DaemonLifecycleService(
        ILogger<DaemonLifecycleService> logger,
        string? daemonExecutablePath = null,
        string? socketPath = null,
        string? pidFilePath = null)
    {
        _logger = logger;
        _daemonExecutablePath = daemonExecutablePath ?? FindDaemonExecutable();
        _socketPath = socketPath ?? GetDefaultSocketPath();
        _pidFilePath = pidFilePath ?? GetDefaultPidFilePath();
    }

    public async Task<bool> IsDaemonRunningAsync()
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

    public async Task<bool> EnsureDaemonRunningAsync()
    {
        if (await IsDaemonRunningAsync())
        {
            State = DaemonState.Running;
            return true;
        }

        return await StartDaemonProcessAsync();
    }

    public async Task<bool> RestartDaemonAsync()
    {
        _logger.LogInformation("Restarting daemon process");
        State = DaemonState.Restarting;

        // Kill existing process if any (via PID file)
        await KillDaemonProcessAsync();

        // Brief delay for cleanup
        await Task.Delay(_restartDelay);

        return await StartDaemonProcessAsync();
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
                if (State != DaemonState.Running)
                    State = DaemonState.Running;
            }
            else
            {
                _consecutiveFailures++;
                _logger.LogWarning("Daemon health check failed (attempt {Count})", _consecutiveFailures);

                State = DaemonState.Unhealthy;
                HealthCheckFailed?.Invoke(this, EventArgs.Empty);

                // Auto-restart after consecutive failures
                if (_consecutiveFailures >= 2 && _consecutiveFailures <= _maxRestartAttempts)
                {
                    _logger.LogInformation("Auto-restarting daemon (attempt {Count})", _consecutiveFailures);
                    await StartDaemonProcessAsync();
                }
                else if (_consecutiveFailures > _maxRestartAttempts)
                {
                    State = DaemonState.Stopped;
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
            State = DaemonState.Stopped;
            return false;
        }

        try
        {
            State = DaemonState.Starting;
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

            // Wire up output readers to prevent pipe buffer deadlock.
            // If stdout/stderr are redirected but never read, the OS pipe buffer
            // fills up and the daemon blocks on its next write — appearing to hang.
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

            _logger.LogInformation("Daemon process started (PID {Pid})", process.Id);

            // Wait for socket to become available
            var socketReady = await WaitForSocketAsync(TimeSpan.FromSeconds(10));
            if (socketReady)
            {
                State = DaemonState.Running;
                _consecutiveFailures = 0;
                _logger.LogInformation("Daemon is ready and accepting connections");
                return true;
            }
            else
            {
                _logger.LogError("Daemon started but socket never became available");
                State = DaemonState.Unhealthy;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start daemon process");
            State = DaemonState.Stopped;
            return false;
        }
    }

    private async Task KillDaemonProcessAsync()
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
                    _logger.LogInformation("Killed daemon process (PID {Pid})", pid);
                }
                catch (ArgumentException)
                {
                    _logger.LogDebug("Daemon process {Pid} already exited", pid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill daemon process via PID file");
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
            return Path.Combine(runDir, "whisper-dictation.sock");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "whisper-dictation", "daemon.sock");
    }

    private static string GetDefaultPidFilePath()
    {
        var runDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runDir))
            return Path.Combine(runDir, "whisper-dictation.pid");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "whisper-dictation", "daemon.pid");
    }

    private static string FindDaemonExecutable()
    {
        // Look relative to the Avalonia app directory (sibling project in build output)
        var baseDir = AppContext.BaseDirectory;

        // Development:../../whisper-dictation-daemon/bin/Debug/net10.0/
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "WhisperDictation.Daemon", "bin", "Debug", "net10.0", "whisper-dictation-daemon"),
            Path.Combine(baseDir, "..", "..", "..", "WhisperDictation.Daemon", "bin", "Release", "net10.0", "whisper-dictation-daemon"),
            // Installed alongside
            Path.Combine(baseDir, "whisper-dictation-daemon"),
            // System install
            "/usr/local/bin/whisper-dictation-daemon",
            "/usr/bin/whisper-dictation-daemon",
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
