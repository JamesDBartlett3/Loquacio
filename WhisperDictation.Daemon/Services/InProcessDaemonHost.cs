using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhisperDictation.Daemon.Ipc;

namespace WhisperDictation.Daemon.Services;

/// <summary>
/// Hosts the daemon (audio pipeline + IPC server) inside another process,
/// e.g. the WPF controller. This is the "in-process daemon" convenience
/// option: instead of launching and supervising a separate daemon executable,
/// the controller runs the same service graph via
/// <see cref="DaemonServiceRegistration.AddWhisperDaemonServices"/> in its own
/// process.
///
/// Controllers running the daemon in-process connect to it over the same IPC
/// transport (named pipe / Unix socket) as they would to an external daemon —
/// the DaemonProxy loopback works identically.
/// </summary>
public sealed class InProcessDaemonHost : IAsyncDisposable
{
    private readonly ILogger<InProcessDaemonHost>? _logger;
    private ServiceProvider? _provider;
    private DaemonPipeline? _pipeline;
    private IpcServer? _ipcServer;
    private HotkeyManager? _hotkeyManager;
    private CancellationTokenSource? _cts;

    /// <summary>True while the in-process daemon is running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>IPC endpoint description of the hosted server (empty when not running).</summary>
    public string Endpoint => _ipcServer?.EndpointPath ?? string.Empty;

    public InProcessDaemonHost(ILogger<InProcessDaemonHost>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Start the in-process daemon: build the DI container and bring up the
    /// IPC server and audio pipeline (same startup order as DaemonHostedService).
    /// </summary>
    /// <param name="socketPathOverride">Optional socket path override (tests).</param>
    /// <param name="configure">Optional extra service registrations applied before the container is built.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task StartAsync(
        string? socketPathOverride = null,
        Action<IServiceCollection>? configure = null,
        CancellationToken ct = default)
    {
        if (IsRunning) return;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWhisperDaemonServices(socketPathOverride);
        configure?.Invoke(services);

        _provider = services.BuildServiceProvider();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _ipcServer = _provider.GetRequiredService<IpcServer>();
        _pipeline = _provider.GetRequiredService<DaemonPipeline>();
        _hotkeyManager = _provider.GetRequiredService<HotkeyManager>();

        try
        {
            await _ipcServer.StartAsync(_cts.Token);
            await _pipeline.InitializeAsync(_cts.Token);
            await _hotkeyManager.InitializeAsync(_cts.Token);
            IsRunning = true;
        }
        catch
        {
            await StopAsync();
            throw;
        }
        _logger?.LogInformation("In-process daemon started — IPC endpoint: {Endpoint}", _ipcServer.EndpointPath);
    }

    /// <summary>
    /// Stop the in-process daemon (same shutdown order as DaemonHostedService)
    /// and dispose the DI container.
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsRunning && _provider is null) return;

        IsRunning = false;

        if (_pipeline is not null)
        {
            try { await _pipeline.ShutdownAsync(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error shutting down in-process pipeline"); }
        }

        if (_ipcServer is not null)
        {
            try { await _ipcServer.StopAsync(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error stopping in-process IPC server"); }
        }

        // Dispose hotkey manager LAST (unregisters system hotkeys) — after the
        // pipeline and IPC server have stopped (PR #7 follow-up #3 ordering fix).
        // Matches DaemonHostedService.StopAsync ordering.
        if (_hotkeyManager is not null)
        {
            try { _hotkeyManager.Dispose(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error disposing in-process hotkey manager"); }
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _pipeline = null;
        _ipcServer = null;
        _hotkeyManager = null;

        if (_provider is not null)
        {
            await _provider.DisposeAsync();
            _provider = null;
        }

        _logger?.LogInformation("In-process daemon stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }
}
