using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhisperDictation.Daemon;
using WhisperDictation.Daemon.Ipc;
using WhisperDictation.Daemon.Services;
using WhisperDictation.Ipc;

namespace WhisperDictation.Tests.Daemon;

/// <summary>
/// Verifies that the daemon DI graph (AddWhisperDaemonServices) is fully
/// resolvable. This catches missing service registrations that would crash the
/// daemon at startup — the exact class of bug these tests were written for
/// (IHotkeyService / IAudioCaptureService / IClipboardService /
/// IKeywordDetectionService were previously unregistered).
/// </summary>
public class DaemonServiceGraphTests
{
    private static string TempSocketPath()
        => Path.Combine(Path.GetTempPath(), $"wd-test-{Guid.NewGuid():N}.sock");

    [Fact]
    public async Task FullGraph_ResolvesAllDaemonServices()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddWhisperDaemonServices(TempSocketPath());

        await using var provider = services.BuildServiceProvider();

        // Act + Assert — every singleton the DaemonPipeline/IpcServer need must resolve
        Assert.NotNull(provider.GetRequiredService<DaemonPipeline>());
        Assert.NotNull(provider.GetRequiredService<IpcServer>());
        Assert.NotNull(provider.GetRequiredService<HotkeyManager>());
        Assert.NotNull(provider.GetRequiredService<WhisperDictation.Services.IActivationManagerService>());
        Assert.NotNull(provider.GetRequiredService<WhisperDictation.Services.IBackgroundTranscriptionService>());
        Assert.NotNull(provider.GetRequiredService<WhisperDictation.Services.IHotkeyService>());
        Assert.NotNull(provider.GetRequiredService<WhisperDictation.Services.IKeywordDetectionService>());
        Assert.NotNull(provider.GetRequiredService<WhisperDictation.Services.IAudioCaptureService>());
        Assert.NotNull(provider.GetRequiredService<WhisperDictation.Services.IClipboardService>());
        Assert.NotNull(provider.GetRequiredService<WhisperDictation.Services.ITextInjectionService>());
        Assert.NotNull(provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
        Assert.NotNull(provider.GetRequiredService<WhisperDictation.Core.Services.IUpdateService>());
    }

    [Fact]
    public void AddWhisperDaemonServices_PlatformAudioCapture_RegistersExactlyOne()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddWhisperDaemonServices(TempSocketPath());

        var audioRegistrations = services
            .Where(d => d.ServiceType == typeof(WhisperDictation.Services.IAudioCaptureService))
            .ToList();

        Assert.Single(audioRegistrations);
    }
}

/// <summary>
/// End-to-end tests for the in-process daemon host: the same service graph as
/// the standalone daemon, hosted inside a controller process.
/// </summary>
public class InProcessDaemonHostTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private InProcessDaemonHost _host = null!;
    private string _socketPath = null!;

    public Task InitializeAsync()
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"wd-host-{Guid.NewGuid():N}.sock");
        _host = new InProcessDaemonHost(LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
            .CreateLogger<InProcessDaemonHost>());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        try { File.Delete(_socketPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task StartStop_Lifecycle()
    {
        Assert.False(_host.IsRunning);

        await _host.StartAsync(socketPathOverride: _socketPath);

        Assert.True(_host.IsRunning);
        if (OperatingSystem.IsWindows())
        {
            // Windows serves a named pipe whose name comes from the override's file name
            Assert.Equal($@"\\.\pipe\{Path.GetFileName(_socketPath)}", _host.Endpoint);
        }
        else
        {
            Assert.Equal(_socketPath, _host.Endpoint);
            Assert.True(File.Exists(_socketPath));
        }

        await _host.StopAsync();

        Assert.False(_host.IsRunning);
    }

    [Fact]
    public async Task StartTwice_IsIdempotent()
    {
        await _host.StartAsync(socketPathOverride: _socketPath);
        await _host.StartAsync(socketPathOverride: _socketPath); // must not throw
        Assert.True(_host.IsRunning);
        await _host.StopAsync();
    }

    [Fact]
    public async Task Controller_CanConnectAndSubscribe()
    {
        // On Linux the in-process daemon serves a Unix socket at the override path.
        if (OperatingSystem.IsWindows()) return; // named pipe path uses fixed pipe name

        await _host.StartAsync(socketPathOverride: _socketPath);

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath));
        await using var stream = new NetworkStream(client, ownsSocket: true);
        using var reader = new StreamReader(stream);
        using var writer = new StreamWriter(stream) { AutoFlush = true };

        // Subscribe → daemon must send a status snapshot back
        var subscribe = JsonSerializer.Serialize(new SubscribeMessage(), JsonOpts);
        await writer.WriteLineAsync(subscribe);

        var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string? line = null;
        while (line is null && !timeoutCts.Token.IsCancellationRequested)
        {
            var readTask = reader.ReadLineAsync(timeoutCts.Token).AsTask();
            var done = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));
            if (done == readTask)
                line = await readTask;
            else
                break;
        }

        Assert.NotNull(line);
        using var doc = JsonDocument.Parse(line!);
        var type = doc.RootElement.GetProperty("type").GetString();
        Assert.Equal("status", type);

        await _host.StopAsync();
    }
}
