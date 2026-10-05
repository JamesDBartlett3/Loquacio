using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Loquacio.Daemon.Ipc;
using Loquacio.Ipc;
using Loquacio.Services;

namespace Loquacio.Tests.Daemon;

/// <summary>
/// Integration tests verifying full round-trip communication between
/// IpcServer (daemon side) and DaemonProxy (controller side).
/// Uses the socket path override constructor to avoid modifying global env vars.
/// </summary>
[Collection("IpcIntegration")]
public class IpcIntegrationTests : IDisposable
{
    private IpcServer? _server;
    private IBackgroundTranscriptionService? _transcriptionSvc;
    private readonly List<DaemonProxy> _proxies = [];
    private string? _socketPath;

    [Fact]
    public async Task FullRoundTrip_StatusBroadcast_ReachesAllClients()
    {
        if (!OperatingSystem.IsLinux()) return;

        await StartServerAsync();

        var proxy1 = await ConnectProxyAsync();
        var proxy2 = await ConnectProxyAsync();

        await Task.Delay(500);

        IpcMessage? msg1 = null;
        IpcMessage? msg2 = null;
        proxy1.SubscribeToUpdates(m => msg1 = m);
        proxy2.SubscribeToUpdates(m => msg2 = m);

        var status = new StatusUpdateMessage
        {
            IsListening = true,
            StatusText = "Integration test listening",
            StatusColor = "#22c55e"
        };

        await _server!.BroadcastMessageAsync(status);

        await WaitForAsync(() => msg1 != null && msg2 != null, TimeSpan.FromSeconds(3));

        Assert.NotNull(msg1);
        Assert.NotNull(msg2);
        Assert.IsType<StatusUpdateMessage>(msg1);
        Assert.IsType<StatusUpdateMessage>(msg2);
        Assert.True(((StatusUpdateMessage)msg1!).IsListening);
        Assert.True(((StatusUpdateMessage)msg2!).IsListening);
        Assert.Equal("Integration test listening", ((StatusUpdateMessage)msg1).StatusText);
    }

    [Fact]
    public async Task FullRoundTrip_SubscribeMessage_TriggersStatusSnapshot()
    {
        if (!OperatingSystem.IsLinux()) return;

        await StartServerAsync();

        IpcMessage? received = null;
        var proxy = await ConnectProxyAsync();
        proxy.SubscribeToUpdates(m => received = m);

        // Server sends initial status on connect — wait for it
        await WaitForAsync(() => received != null, TimeSpan.FromSeconds(2));

        if (received == null)
        {
            // If initial snapshot missed, send explicit subscribe
            await proxy.SendCommandAsync(new SubscribeMessage());
            await WaitForAsync(() => received != null, TimeSpan.FromSeconds(3));
        }

        Assert.NotNull(received);
        Assert.IsType<StatusUpdateMessage>(received);
    }

    [Fact]
    public async Task FullRoundTrip_GetStatusCommand_ReturnsStatus()
    {
        if (!OperatingSystem.IsLinux()) return;

        await StartServerAsync();
        var proxy = await ConnectProxyAsync();
        await Task.Delay(300);

        var cmd = new GetStatusMessage { CorrelationId = "test-corr-123" };
        var response = await proxy.SendCommandAsync(cmd);

        Assert.NotNull(response);
        Assert.Equal("test-corr-123", response.CorrelationId);
    }

    [Fact]
    public async Task FullRoundTrip_CancelDictation_NoActiveRun_Acknowledged()
    {
        if (!OperatingSystem.IsLinux()) return;

        await StartServerAsync();
        var proxy = await ConnectProxyAsync();

        _transcriptionSvc!.CancelActiveRun().Returns(false);

        var ack = await proxy.CancelDictationAsync();

        Assert.True(ack.Success);
        Assert.Contains("no active run", ack.Error);
        _transcriptionSvc.Received(1).CancelActiveRun();
    }

    [Fact]
    public async Task FullRoundTrip_CancelDictation_ActiveRun_Reported()
    {
        if (!OperatingSystem.IsLinux()) return;

        await StartServerAsync();
        var proxy = await ConnectProxyAsync();

        _transcriptionSvc!.CancelActiveRun().Returns(true);

        var ack = await proxy.CancelDictationAsync();

        Assert.True(ack.Success);
        Assert.Contains("cancelled active dictation run", ack.Error);
    }

    [Fact]
    public async Task FullRoundTrip_TranscriptionBroadcast_DeliveredToSubscriber()
    {
        if (!OperatingSystem.IsLinux()) return;

        await StartServerAsync();
        var proxy = await ConnectProxyAsync();
        await Task.Delay(300);

        IpcMessage? received = null;
        proxy.SubscribeToUpdates(m => received = m);

        var transcription = new TranscriptionResultMessage
        {
            Text = "Hello from the integration test",
            Timestamp = DateTimeOffset.UtcNow,
            IsFinal = true
        };

        await _server!.BroadcastMessageAsync(transcription);

        await WaitForAsync(() => received != null, TimeSpan.FromSeconds(3));

        Assert.NotNull(received);
        Assert.IsType<TranscriptionResultMessage>(received);
        Assert.Equal("Hello from the integration test", ((TranscriptionResultMessage)received).Text);
        Assert.True(((TranscriptionResultMessage)received).IsFinal);
    }

    [Fact]
    public async Task FullRoundTrip_StartStopListening_AckReturned()
    {
        if (!OperatingSystem.IsLinux()) return;

        await StartServerAsync();
        var proxy = await ConnectProxyAsync();
        await Task.Delay(300);

        var response = await proxy.SendCommandAsync(new StartListeningMessage());

        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    // ── Helpers ──

    private async Task StartServerAsync()
    {
        // Use unique temp path — no env var modification needed
        var testDir = Path.Combine(Path.GetTempPath(), $"wd-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        _socketPath = Path.Combine(testDir, "loquacio.sock");

        // Register mocked services required by IpcServer
        var services = new ServiceCollection();

        var activationMgr = Substitute.For<IActivationManagerService>();
        activationMgr.IsListening.Returns(false);
        activationMgr.CurrentMode.Returns(ActivationMode.Continuous);

        var historySvc = Substitute.For<IHistoryService>();
        historySvc.GetRecentHistoryAsync(Arg.Any<int>())
            .Returns(new List<HistoryEntry>());

        var transcriptionSvc = Substitute.For<IBackgroundTranscriptionService>();
        transcriptionSvc.PipelineState.Returns(Loquacio.Infrastructure.DictationState.Idle);
        transcriptionSvc.PipelineSessionId.Returns((Guid?)null);

        services.AddSingleton(activationMgr);
        services.AddSingleton(historySvc);
        services.AddSingleton(transcriptionSvc);
        _transcriptionSvc = transcriptionSvc;

        _server = new IpcServer(
            services.BuildServiceProvider(),
            NullLogger<IpcServer>.Instance,
            socketPathOverride: _socketPath);

        await _server.StartAsync();
        await Task.Delay(100);
    }

    private async Task<DaemonProxy> ConnectProxyAsync()
    {
        var proxy = new DaemonProxy(NullLogger<DaemonProxy>.Instance);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath!));

        var stream = new NetworkStream(socket, ownsSocket: true);
        var reader = new StreamReader(stream);
        var writer = new StreamWriter(stream) { AutoFlush = true };

        var proxyType = typeof(DaemonProxy);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        proxyType.GetField("_socket", flags)?.SetValue(proxy, socket);
        proxyType.GetField("_reader", flags)?.SetValue(proxy, reader);
        proxyType.GetField("_writer", flags)?.SetValue(proxy, writer);

        var readCts = new CancellationTokenSource();
        proxyType.GetField("_readCts", flags)?.SetValue(proxy, readCts);

        var readMethod = proxyType.GetMethod("ReadLoopAsync", flags);
        if (readMethod != null)
        {
            var readTask = Task.Run(() => (Task)readMethod.Invoke(proxy, new object[] { readCts.Token })!);
            proxyType.GetField("_readTask", flags)?.SetValue(proxy, readTask);
        }

        _proxies.Add(proxy);
        return proxy;
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
    }

    public void Dispose()
    {
        foreach (var p in _proxies)
        {
            try { p.Dispose(); } catch { }
        }
        _proxies.Clear();

        try { _server?.StopAsync().GetAwaiter().GetResult(); } catch { }
        try { if (_server is not null) _server.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }

        // Clean up temp socket dir
        if (_socketPath != null)
        {
            try
            {
                var dir = Path.GetDirectoryName(_socketPath);
                if (dir != null && Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch { }
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// xUnit collection definition to serialize IPC integration tests.
/// </summary>
[CollectionDefinition("IpcIntegration")]
public class IpcIntegrationCollection { }
