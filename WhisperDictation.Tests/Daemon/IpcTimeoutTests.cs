using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperDictation.Daemon.Ipc;
using WhisperDictation.Ipc;
using WhisperDictation.Services;

namespace WhisperDictation.Tests.Daemon;

/// <summary>
/// Regression tests for the "Cycle Mode hangs the app AND daemon" bug:
/// 1. A daemon-side handler that never completes must not hang the connection
///    forever — the server returns a timeout Ack within HandlerTimeout.
/// 2. The controller-side proxy must not leak pending command waiters on timeout.
/// </summary>
public class IpcTimeoutTests : IDisposable
{
    private IpcServer? _server;
    private string? _socketPath;

    [Fact]
    public async Task HungHandler_ReturnsTimeoutAck_WithinHandlerTimeout()
    {
        if (!OperatingSystem.IsLinux()) return;

        var originalTimeout = IpcServer.HandlerTimeout;
        IpcServer.HandlerTimeout = TimeSpan.FromSeconds(2);
        try
        {
            var neverCompletes = new TaskCompletionSource();
            var activation = Substitute.For<IActivationManagerService>();
            activation.SwitchModeAsync(Arg.Any<ActivationMode>(), Arg.Any<CancellationToken>())
                .Returns(_ => neverCompletes.Task); // simulates the WASAPI deadlock hang

            await StartServerAsync(activation);

            using var proxy = new DaemonProxy(socketPathOverride: _socketPath);
            await proxy.ConnectAsync();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ack = await proxy.SendCommandAsync(new SetModeMessage { Mode = ActivationMode.PushToTalk });
            sw.Stop();

            Assert.False(ack.Success);
            Assert.Contains("timed out", ack.Error, StringComparison.OrdinalIgnoreCase);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8),
                $"Ack took {sw.Elapsed} — handler timeout did not fire");
        }
        finally
        {
            IpcServer.HandlerTimeout = originalTimeout;
        }
    }

    [Fact]
    public async Task ProxyTimeout_RemovesPendingWaiter()
    {
        if (!OperatingSystem.IsLinux()) return;

        var originalTimeout = IpcServer.HandlerTimeout;
        IpcServer.HandlerTimeout = TimeSpan.FromSeconds(60); // server never times out — proxy's 10s rules
        try
        {
            var neverCompletes = new TaskCompletionSource();
            var activation = Substitute.For<IActivationManagerService>();
            activation.SwitchModeAsync(Arg.Any<ActivationMode>(), Arg.Any<CancellationToken>())
                .Returns(_ => neverCompletes.Task);

            await StartServerAsync(activation);

            using var proxy = new DaemonProxy(socketPathOverride: _socketPath);
            await proxy.ConnectAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                proxy.SendCommandAsync(new SetModeMessage { Mode = ActivationMode.PushToTalk }));

            var pendingField = typeof(DaemonProxy).GetField("_pending",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(pendingField);
            var pending = (System.Collections.IDictionary)pendingField!.GetValue(proxy)!;
            Assert.Empty(pending); // regression: timed-out commands must not leak waiters
        }
        finally
        {
            IpcServer.HandlerTimeout = originalTimeout;
        }
    }

    private async Task StartServerAsync(IActivationManagerService activation)
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"wd-ipc-timeout-{Guid.NewGuid():N}.sock");
        var services = new ServiceCollection()
            .AddSingleton(activation)
            .BuildServiceProvider();
        _server = new IpcServer(services, NullLogger<IpcServer>.Instance, _socketPath);
        await _server.StartAsync();
    }

    public void Dispose()
    {
        try { _server?.StopAsync().Wait(TimeSpan.FromSeconds(5)); } catch { }
        _server = null;
    }
}
