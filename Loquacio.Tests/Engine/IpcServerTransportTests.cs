using System.Reflection;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Loquacio.Engine.Ipc;
using Loquacio.Ipc;

namespace Loquacio.Tests.Engine;

/// <summary>
/// Mock implementation of IActivationManagerService for testing.
/// </summary>
internal class MockActivationManagerService : Loquacio.Services.IActivationManagerService
{
    public Loquacio.Services.ActivationMode CurrentMode => Loquacio.Services.ActivationMode.Continuous;
    public bool IsListening => false;
    
    public event EventHandler<bool>? ListeningStateChanged;
    public event EventHandler<Loquacio.Services.ActivationMode>? ModeChanged;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SwitchModeAsync(Loquacio.Services.ActivationMode mode, CancellationToken ct = default) => Task.CompletedTask;
    public Task ToggleListeningAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CycleModeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopListeningAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task OnPushToTalkStartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task OnPushToTalkEndAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ApplyActivationSettingsAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public void Dispose() { }
}

/// <summary>
/// Tests for the dual-transport IPC server (Unix sockets + Windows named pipes).
/// Verifies endpoint path generation, transport selection, and broadcast logic.
/// </summary>
public class IpcServerTransportTests
{
    [Fact]
    public void EndpointPath_ReturnsNonEmptyString()
    {
        var server = CreateServer();
        var path = InvokeInstanceMethod<string>(server, "GetEndpointDescription");
        Assert.NotNull(path);
        Assert.False(string.IsNullOrEmpty(path));
    }

    [Fact]
    public void EndpointPath_OnLinux_ReturnsUnixSocketPath()
    {
        if (!OperatingSystem.IsLinux()) return;

        var server = CreateServer();
        var path = InvokeInstanceMethod<string>(server, "GetEndpointDescription");
        Assert.NotNull(path);
        Assert.Contains("loquacio", path);
        Assert.EndsWith(".sock", path);
    }

    [Fact]
    public void EndpointPath_OnWindows_ReturnsNamedPipePath()
    {
        if (!OperatingSystem.IsWindows()) return;

        var server = CreateServer();
        var path = InvokeInstanceMethod<string>(server, "GetEndpointDescription");
        Assert.NotNull(path);
        Assert.Contains("loquacio", path);
        Assert.Contains("\\\\.\\pipe\\", path);
    }

    [Fact]
    public void UnixSocketPath_ReturnsValidPathWithSockExtension()
    {
        var path = InvokeStaticMethod<string>("GetDefaultUnixSocketPath");
        Assert.Contains("loquacio", path);
        Assert.EndsWith(".sock", path);
    }

    [Fact]
    public void UnixSocketPath_PrefersXdgRuntimeDir_WhenSet()
    {
        // XDG_RUNTIME_DIR is not set in containers — set it explicitly so the
        // test holds everywhere (desktop hosts, Docker CI).
        var original = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        try
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", "/tmp/test-runtime");
            var path = InvokeStaticMethod<string>("GetDefaultUnixSocketPath");
            Assert.NotNull(path);
            Assert.Equal(Path.Combine("/tmp/test-runtime", "loquacio.sock"), path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", original);
        }
    }

    [Fact]
    public async Task BroadcastMessage_WithNoClients_DoesNotThrow()
    {
        var server = CreateServer();

        // Should not throw even with zero clients
        await server.BroadcastMessageAsync(new StatusUpdateMessage());
    }

    [Fact]
    public async Task StartStop_WithoutRealConnection_DoesNotThrow()
    {
        // Use a UNIQUE socket path — binding the shared default path races the
        // parallel EngineLifecycleServiceTests engine probe (PR #7 follow-up #3
        // flake reproduced on main).
        var testSocketPath = $"/tmp/wd-startstop-{Guid.NewGuid():N}.sock";
        var services = new ServiceCollection().BuildServiceProvider();
        var server = new IpcServer(services, NullLogger<IpcServer>.Instance, socketPathOverride: testSocketPath);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        // Start and stop quickly — the accept loop should exit cleanly
        await server.StartAsync(cts.Token);
        await Task.Delay(50);
        await server.StopAsync();
        Assert.False(File.Exists(testSocketPath), "Socket file should be removed after stop");
    }

    [Fact]
    public async Task StartStop_OnLinux_CleansUpSocketFile()
    {
        if (!OperatingSystem.IsLinux()) return;

        var testDir = Path.Combine(Path.GetTempPath(), $"wd-transport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        var testSocketPath = Path.Combine(testDir, "loquacio.sock");

        try
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var server = new IpcServer(services, NullLogger<IpcServer>.Instance, socketPathOverride: testSocketPath);

            await server.StartAsync();
            Assert.True(File.Exists(testSocketPath), "Socket file should exist while running");

            await server.StopAsync();
            Assert.False(File.Exists(testSocketPath), "Socket file should be removed after stop");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    [Fact]
    public async Task FrameIntegrity_InterleavedUnicastAndBroadcast_NoCorruption()
    {
        if (!OperatingSystem.IsLinux()) return;

        // Real race probe (PR #7 follow-up #1): a slow-reading client with a small
        // receive buffer floods unicast `get-status` requests (server replies go
        // through SendMessageAsync) while large concurrent broadcasts fire
        // (BroadcastMessageAsync writes raw bytes). Both paths contend on the same
        // per-stream semaphore; pre-fix code (PR #6 race class) corrupted frames
        // under exactly this interleaving. Assert every received frame parses.

        var testSocketPath = $"/tmp/wd-frame-integrity-{Guid.NewGuid():N}.sock";
        var services = new ServiceCollection();

        // Register a mock IActivationManagerService for GetCurrentStatus()
        services.AddSingleton<Loquacio.Services.IActivationManagerService, MockActivationManagerService>();

        var serviceProvider = services.BuildServiceProvider();
        var server = new IpcServer(serviceProvider, NullLogger<IpcServer>.Instance, socketPathOverride: testSocketPath);

        var receivedFrames = new System.Collections.Concurrent.ConcurrentQueue<string>();
        int unicastAcks = 0;
        var clientCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var clientTask = Task.Run(async () =>
        {
            // Give the server time to start
            await Task.Delay(100);

            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(testSocketPath));
            using var stream = new NetworkStream(client, ownsSocket: true);

            // Slow reader: raw socket reads with a small buffer to force partial
            // writes / backpressure, widening the interleaving window.
            var receiveBufferSize = 4096;
            client.ReceiveBufferSize = receiveBufferSize;
            var buffer = new byte[receiveBufferSize];
            var pending = new System.Text.StringBuilder();
            var readerDone = new TaskCompletionSource();

            var readerTask = Task.Run(async () =>
            {
                try
                {
                    while (!clientCts.Token.IsCancellationRequested)
                    {
                        var read = await stream.ReadAsync(buffer, clientCts.Token);
                        if (read == 0) break; // stream closed
                        pending.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
                        string text;
                        lock (pending) text = pending.ToString();
                        int nl;
                        while ((nl = text.IndexOf('\n')) >= 0)
                        {
                            var frame = text[..nl];
                            var rest = text[(nl + 1)..];
                            lock (pending) { pending.Clear(); pending.Append(rest); }
                            text = rest;
                            if (frame.Length > 0)
                            {
                                // get-status responses carry the request's correlationId (SendMessageAsync path)
                                if (frame.Contains("\"id\":\"probe-")) Interlocked.Increment(ref unicastAcks);
                                receivedFrames.Enqueue(frame);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                finally { readerDone.TrySetResult(); }
            });

            // Unicast flood: real client requests -> real SendMessageAsync responses.
            // Each line may be sent while a broadcast frame is mid-write.
            for (int i = 0; i < 300; i++)
            {
                var line = $"{{\"type\":\"get-status\",\"id\":\"probe-{i}\"}}\n";
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(line), clientCts.Token);
                await stream.FlushAsync(clientCts.Token);
            }

            await readerDone.Task;
        });

        await server.StartAsync();

        // Give client time to connect and receive initial snapshot
        await Task.Delay(200);

        // Concurrent LARGE broadcasts (~256 KB each) - forces multi-write frames,
        // partial writes, and backpressure against the slow reader.
        var bigPayload = new string('x', 256 * 1024);
        var broadcastTasks = new List<Task>();
        for (int i = 0; i < 60; i++)
        {
            int idx = i;
            broadcastTasks.Add(Task.Run(async () =>
            {
                await Task.Delay(Random.Shared.Next(0, 50));
                await server.BroadcastMessageAsync(new StatusUpdateMessage
                {
                    IsListening = idx % 2 == 0,
                    Mode = idx % 3 == 0 ? ActivationMode.Continuous : ActivationMode.PushToTalk,
                    StatusText = $"broadcast {idx}: {bigPayload}",
                    StatusColor = idx % 2 == 0 ? "#22c55e" : "#808080"
                });
            }));
        }

        await Task.WhenAll(broadcastTasks);

        // Give client time to drain the socket
        await Task.Delay(1000);

        clientCts.Cancel();
        await server.StopAsync();
        try { await clientTask; } catch (OperationCanceledException) { }

        // Verify ALL frames are valid JSON with a 'type' property - any splice
        // between a unicast frame and a broadcast frame fails here.
        var frames = receivedFrames.ToArray();
        foreach (var frame in frames)
        {
            try
            {
                using var doc = JsonDocument.Parse(frame);
                Assert.True(doc.RootElement.TryGetProperty("type", out _),
                    $"Frame should have 'type' property: {frame[..Math.Min(200, frame.Length)]}...");
            }
            catch (JsonException ex)
            {
                Assert.Fail($"Received corrupted frame (not valid JSON): {frame[..Math.Min(200, frame.Length)]}...\nException: {ex.Message}");
            }
        }

        // The probe must actually exercise both paths: real unicast acks and broadcasts.
        Assert.True(unicastAcks >= 100,
            $"Expected >=100 real unicast acks (got {unicastAcks}) - probe did not exercise SendMessageAsync");
        Assert.True(frames.Length >= unicastAcks + 60,
            $"Expected at least {unicastAcks + 60} frames (acks + broadcasts + snapshot), got {frames.Length}");
    }

    private static IpcServer CreateServer()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<IpcServer>.Instance;
        return new IpcServer(services, logger);
    }

    /// <summary>
    /// Invoke a private static method on IpcServer via reflection.
    /// </summary>
    private static T? InvokeStaticMethod<T>(string methodName)
    {
        var method = typeof(IpcServer).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        return method != null ? (T?)method.Invoke(null, null) : default;
    }

    /// <summary>
    /// Invoke a private instance method on IpcServer via reflection.
    /// </summary>
    private static T? InvokeInstanceMethod<T>(IpcServer instance, string methodName)
    {
        var method = typeof(IpcServer).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        return method != null ? (T?)method.Invoke(instance, null) : default;
    }
}
