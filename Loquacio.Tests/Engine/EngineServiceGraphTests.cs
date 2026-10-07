using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Loquacio.Engine;
using Loquacio.Engine.Ipc;
using Loquacio.Engine.Services;
using Loquacio.Ipc;

namespace Loquacio.Tests.Engine;

/// <summary>
/// Verifies that the engine DI graph (AddWhisperEngineServices) is fully
/// resolvable. This catches missing service registrations that would crash the
/// engine at startup — the exact class of bug these tests were written for
/// (IHotkeyService / IAudioCaptureService / IClipboardService /
/// IKeywordDetectionService were previously unregistered).
/// </summary>
public class EngineServiceGraphTests
{
    private static string TempSocketPath()
        => Path.Combine(Path.GetTempPath(), $"wd-test-{Guid.NewGuid():N}.sock");

    [Fact]
    public async Task FullGraph_ResolvesAllEngineServices()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddWhisperEngineServices(TempSocketPath());

        await using var provider = services.BuildServiceProvider();

        // Act + Assert — every singleton the EnginePipeline/IpcServer need must resolve
        Assert.NotNull(provider.GetRequiredService<EnginePipeline>());
        Assert.NotNull(provider.GetRequiredService<IpcServer>());
        Assert.NotNull(provider.GetRequiredService<HotkeyManager>());
        Assert.NotNull(provider.GetRequiredService<Loquacio.Services.IActivationManagerService>());
        Assert.NotNull(provider.GetRequiredService<Loquacio.Services.IBackgroundTranscriptionService>());
        Assert.NotNull(provider.GetRequiredService<Loquacio.Services.IHotkeyService>());
        Assert.NotNull(provider.GetRequiredService<Loquacio.Services.IKeywordDetectionService>());
        Assert.NotNull(provider.GetRequiredService<Loquacio.Services.IAudioCaptureService>());
        Assert.NotNull(provider.GetRequiredService<Loquacio.Services.IClipboardService>());
        Assert.NotNull(provider.GetRequiredService<Loquacio.Services.ITextInjectionService>());
        Assert.NotNull(provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
        Assert.NotNull(provider.GetRequiredService<Loquacio.Core.Services.IUpdateService>());
    }

    [Fact]
    public void AddWhisperEngineServices_PlatformAudioCapture_RegistersExactlyOne()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddWhisperEngineServices(TempSocketPath());

        var audioRegistrations = services
            .Where(d => d.ServiceType == typeof(Loquacio.Services.IAudioCaptureService))
            .ToList();

        Assert.Single(audioRegistrations);
    }
}

/// <summary>
/// End-to-end tests for the in-process engine host: the same service graph as
/// the standalone engine, hosted inside a controller process.
/// </summary>
public class InProcessEngineHostTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private InProcessEngineHost _host = null!;
    private string _socketPath = null!;

    public Task InitializeAsync()
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"wd-host-{Guid.NewGuid():N}.sock");
        _host = new InProcessEngineHost(LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
            .CreateLogger<InProcessEngineHost>());
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
        // On Linux the in-process engine serves a Unix socket at the override path.
        if (OperatingSystem.IsWindows()) return; // named pipe path uses fixed pipe name

        await _host.StartAsync(socketPathOverride: _socketPath);

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath));
        await using var stream = new NetworkStream(client, ownsSocket: true);
        using var reader = new StreamReader(stream);
        using var writer = new StreamWriter(stream) { AutoFlush = true };

        // Subscribe → engine must send a status snapshot back
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
