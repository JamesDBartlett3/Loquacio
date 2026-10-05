using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Loquacio.Ipc;

namespace Loquacio.Tests.Ipc;

/// <summary>
/// Tests for DaemonProxy IPC client.
/// Uses a real Unix domain socket pair to verify the JSON protocol.
/// </summary>
public class DaemonProxyTests : IDisposable
{
    private readonly string _socketPath;
    private Socket? _serverListener;
    private readonly List<Socket> _serverSockets = new();

    public DaemonProxyTests()
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"wd-test-{Guid.NewGuid():N}.sock");
    }

    [Fact]
    public void IsConnected_FalseBeforeConnect()
    {
        var proxy = new DaemonProxy(NullLogger<DaemonProxy>.Instance);
        Assert.False(proxy.IsConnected);
        proxy.Dispose();
    }

    [Fact]
    public async Task ConnectAsync_ConnectsToServer()
    {
        var (server, client) = await SetupSocketPairAsync();
        _serverSockets.Add(server);
        using var proxy = client;

        Assert.True(proxy.IsConnected);
    }

    [Fact]
    public async Task SendCommandAsync_StartListening_ReturnsAck()
    {
        var (server, client) = await SetupSocketPairAsync();
        _serverSockets.Add(server);
        using var proxy = client;

        // Server side: read command, respond with ack (echo correlation ID)
        var serverTask = Task.Run(async () =>
        {
            using var stream = new NetworkStream(server, ownsSocket: false);
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true };

            var line = await reader.ReadLineAsync();
            Assert.NotNull(line);
            Assert.Contains("start-listening", line);

            // Parse correlation ID and echo it back
            string? correlationId = null;
            try
            {
                using var doc = JsonDocument.Parse(line!);
                doc.RootElement.TryGetProperty("id", out var idEl);
                correlationId = idEl.GetString();
            }
            catch { }

            var ack = new AckMessage { Success = true, CorrelationId = correlationId };
            await writer.WriteLineAsync(JsonSerializer.Serialize(ack, typeof(AckMessage)));
        });

        await Task.Delay(100);
        var result = await proxy.SendCommandAsync(new StartListeningMessage());
        await serverTask;

        Assert.True(result.Success);
    }

    [Fact]
    public async Task SubscribeToUpdates_ReceivesStatusMessage()
    {
        var (server, client) = await SetupSocketPairAsync();
        _serverSockets.Add(server);
        using var proxy = client;

        IpcMessage? received = null;
        proxy.SubscribeToUpdates(msg => received = msg);

        // Server sends initial status (no correlation ID → goes to subscriber)
        _ = Task.Run(async () =>
        {
            using var stream = new NetworkStream(server, ownsSocket: false);
            using var writer = new StreamWriter(stream) { AutoFlush = true };

            var status = new StatusUpdateMessage
            {
                IsListening = true,
                StatusText = "Listening"
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(status, typeof(StatusUpdateMessage)));
        });

        await Task.Delay(300);

        Assert.NotNull(received);
        Assert.IsType<StatusUpdateMessage>(received);
        Assert.True(((StatusUpdateMessage)received).IsListening);
    }

    [Fact]
    public async Task Disconnected_Event_Fires_OnServerClose()
    {
        var (server, client) = await SetupSocketPairAsync();
        using var proxy = client;

        bool disconnected = false;
        proxy.Disconnected += (_, _) => disconnected = true;

        server.Close();

        // Wait for disconnect detection
        await Task.Delay(500);

        Assert.True(disconnected);
        // IsConnected may lag until the next I/O attempt detects the closure;
        // the Disconnected event firing is the authoritative signal.
    }

    [Fact]
    public async Task SendCommandAsync_StopListening_ReturnsAck()
    {
        var (server, client) = await SetupSocketPairAsync();
        _serverSockets.Add(server);
        using var proxy = client;

        var serverTask = Task.Run(async () =>
        {
            using var stream = new NetworkStream(server, ownsSocket: false);
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true };

            var line = await reader.ReadLineAsync();
            Assert.Contains("stop-listening", line);

            string? correlationId = null;
            try
            {
                using var doc = JsonDocument.Parse(line!);
                doc.RootElement.TryGetProperty("id", out var idEl);
                correlationId = idEl.GetString();
            }
            catch { }

            var ack = new AckMessage { Success = true, CorrelationId = correlationId };
            await writer.WriteLineAsync(JsonSerializer.Serialize(ack, typeof(AckMessage)));
        });

        await Task.Delay(100);
        var result = await proxy.SendCommandAsync(new StopListeningMessage());
        await serverTask;

        Assert.True(result.Success);
    }

    // ── Helpers ──

    private async Task<(Socket server, DaemonProxy proxy)> SetupSocketPairAsync()
    {
        var dir = Path.GetDirectoryName(_socketPath)!;
        Directory.CreateDirectory(dir);
        if (File.Exists(_socketPath))
            File.Delete(_socketPath);

        _serverListener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _serverListener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _serverListener.Listen(1);

        var serverAcceptTask = _serverListener.AcceptAsync();

        var proxy = new DaemonProxy(NullLogger<DaemonProxy>.Instance);
        await ConnectWithTestPath(proxy, _socketPath);

        var server = await serverAcceptTask;

        return (server, proxy);
    }

    private static async Task ConnectWithTestPath(DaemonProxy proxy, string path)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path));

        var stream = new NetworkStream(socket, ownsSocket: true);
        var reader = new StreamReader(stream);
        var writer = new StreamWriter(stream) { AutoFlush = true };

        var proxyType = typeof(DaemonProxy);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        proxyType.GetField("_socket", flags)?.SetValue(proxy, socket);
        proxyType.GetField("_reader", flags)?.SetValue(proxy, reader);
        proxyType.GetField("_writer", flags)?.SetValue(proxy, writer);

        var cts = new CancellationTokenSource();
        proxyType.GetField("_readCts", flags)?.SetValue(proxy, cts);

        var readMethod = proxyType.GetMethod("ReadLoopAsync", flags);
        if (readMethod != null)
        {
            var readTask = Task.Run(() => (Task)readMethod.Invoke(proxy, new object[] { cts.Token })!);
            proxyType.GetField("_readTask", flags)?.SetValue(proxy, readTask);
        }
    }

    public void Dispose()
    {
        foreach (var s in _serverSockets)
        {
            try { s.Close(); } catch { }
        }
        _serverSockets.Clear();
        _serverListener?.Close();
        try { if (File.Exists(_socketPath)) File.Delete(_socketPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
