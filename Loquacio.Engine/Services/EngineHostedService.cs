using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Loquacio.Core.Services;
using Loquacio.Engine.Ipc;

namespace Loquacio.Engine.Services;

/// <summary>
/// Hosted service that starts and stops the engine pipeline, IPC server, and hotkey manager.
/// </summary>
public sealed class EngineHostedService(
    EnginePipeline pipeline,
    IpcServer ipcServer,
    HotkeyManager hotkeyManager,
    IUpdateService updateService,
    ILogger<EngineHostedService> logger
) : IHostedService
{
    private CancellationTokenSource? _cts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        logger.LogInformation("Starting Loquacio engine…");

        // Start the IPC server (accepts controller connections)
        await ipcServer.StartAsync(_cts.Token);

        // Start the audio pipeline
        await pipeline.InitializeAsync(_cts.Token);

        // Initialize global hotkeys (platform-specific: X11 on Linux, Win32 on Windows)
        await hotkeyManager.InitializeAsync(_cts.Token);

        logger.LogInformation("Engine ready. IPC endpoint: {Endpoint}", ipcServer.EndpointPath);

        // Fire-and-forget update check (Phase D auto-update). Never blocks or
        // crashes startup on network failure.
        _ = Task.Run(async () =>
        {
            try
            {
                var update = await updateService.CheckForUpdatesAsync(cancellationToken);
                if (update is not null)
                {
                    logger.LogInformation("Update available: v{Version}. Run 'status' or the controller UI for details.", update.Version);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Update check failed (non-fatal)");
            }
        });
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping engine…");
        _cts?.Cancel();

        await pipeline.ShutdownAsync();
        await ipcServer.StopAsync();

        // Dispose hotkey manager LAST (unregisters system hotkeys). Guarded:
        // a hotkey-backend failure during teardown must not abort the rest of
        // the shutdown sequence (PR #7 follow-up #3).
        try
        {
            hotkeyManager.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error disposing hotkey manager during shutdown");
        }

        _cts?.Dispose();

        logger.LogInformation("Engine stopped.");
    }
}
