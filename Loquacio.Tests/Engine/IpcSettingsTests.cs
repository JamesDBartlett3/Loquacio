using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Loquacio.Engine.Services;
using Loquacio.Ipc;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.Tests.Engine;

/// <summary>
/// End-to-end tests for the engine settings pathway over IPC:
/// get-settings / get-devices / update-settings (round-trip + settings-changed broadcast).
/// </summary>
public class IpcSettingsTests : IAsyncLifetime
{
    private InProcessEngineHost? _host;
    private EngineProxy? _proxy;
    private readonly string _socketPath = Path.Combine(Path.GetTempPath(), $"wd-ipc-settings-{Guid.NewGuid():N}.sock");

    private sealed class InMemorySettingsService : ISettingsService
    {
        public Settings Settings { get; set; } = new();
        public event EventHandler? SettingsChanged;
        public Task<Settings> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);
        public Task SaveSettingsAsync(Settings settings, CancellationToken ct = default)
        {
            Settings = settings;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudioCapture : IAudioCaptureService
    {
        public event EventHandler<AudioSegment>? OnSegmentCaptured { add { } remove { } }
        public event EventHandler<double>? OnAudioLevel { add { } remove { } }
        public event EventHandler<Exception>? OnError { add { } remove { } }

        public IReadOnlyList<AudioDevice> GetAvailableDevices() =>
        [
            new AudioDevice { Id = "dev-1", FriendlyName = "Test Mic 1", IsCaptureDevice = true },
            new AudioDevice { Id = "dev-2", FriendlyName = "Test Mic 2", IsCaptureDevice = true },
        ];

        public void StartCapture(AudioDevice device, double silenceThresholdMs = 1500, double silenceThresholdDb = -40, double gain = 1.0,
            bool compressorEnabled = true, double compressorThresholdDb = -18, double compressorRatio = 4.0) { }
        public void StopCapture() { }
        public bool IsCapturing => false;
    }

    private async Task<(EngineProxy proxy, InMemorySettingsService settings)> StartAsync()
    {
        var settings = new InMemorySettingsService();
        var activation = Substitute.For<IActivationManagerService>();

        _host = new InProcessEngineHost(NullLogger<InProcessEngineHost>.Instance);
        await _host.StartAsync(_socketPath, services =>
        {
            services.RemoveAll<ISettingsService>();
            services.AddSingleton<ISettingsService>(settings);
            services.RemoveAll<IAudioCaptureService>();
            services.AddSingleton<IAudioCaptureService, FakeAudioCapture>();
            services.RemoveAll<IActivationManagerService>();
            services.AddSingleton(activation);
        });

        _proxy = new EngineProxy(socketPathOverride: _socketPath);
        await _proxy.ConnectAsync();
        return (_proxy, settings);
    }

    [Fact]
    public async Task GetSettings_ReturnsSnapshot()
    {
        var (proxy, settings) = await StartAsync();

        settings.Settings.Audio.DeviceId = "dev-2";

        var snap = await proxy.GetSettingsAsync();

        Assert.NotNull(snap);
        Assert.Equal("dev-2", snap!.Audio.DeviceId);
    }

    [Fact]
    public async Task GetDevices_ReturnsEngineDeviceList()
    {
        var (proxy, _) = await StartAsync();

        var devices = await proxy.GetDevicesAsync();

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, d => d.Id == "dev-1" && d.FriendlyName == "Test Mic 1");
    }

    [Fact]
    public async Task UpdateSettings_PersistsSections_AndBroadcastsChange()
    {
        var (proxy, settings) = await StartAsync();
        var modelPath = Path.GetTempFileName(); // must exist — validated server-side

        IpcMessage? broadcast = null;
        proxy.SubscribeToUpdates(m => { if (m is SettingsChangedMessage) broadcast = m; });

        var updated = new Settings();
        updated.Audio.DeviceId = "dev-2";
        updated.Whisper.ModelPath = modelPath;
        updated.Activation.Hotkey = "Ctrl+Shift+Space";
        updated.Activation.Keyword = "Hey Computer";
        updated.Output.Mode = "inject";

        var ack = await proxy.UpdateSettingsAsync(updated);

        Assert.True(ack.Success, ack.Error);

        // Sections were persisted on the engine side
        Assert.Equal("dev-2", settings.Settings.Audio.DeviceId);
        Assert.Equal(modelPath, settings.Settings.Whisper.ModelPath);
        Assert.Equal("Ctrl+Shift+Space", settings.Settings.Activation.Hotkey);
        Assert.Equal("Hey Computer", settings.Settings.Activation.Keyword);
        Assert.Equal("inject", settings.Settings.Output.Mode);

        // Round-trip: engine serves the updated settings back
        var snap = await proxy.GetSettingsAsync();
        Assert.Equal("dev-2", snap!.Audio.DeviceId);

        // Other controllers were notified
        Assert.NotNull(broadcast);
    }

    [Fact]
    public async Task UpdateSettings_UnknownSection_ReturnsError()
    {
        var (proxy, _) = await StartAsync();

        var cmd = new UpdateSettingsMessage { Settings = { ["bogus"] = new { x = 1 } } };
        var ack = await proxy.SendCommandAsync(cmd);

        Assert.False(ack.Success);
        Assert.Contains("bogus", ack.Error);
    }

    [Fact]
    public async Task UpdateSettings_UnknownDeviceId_RejectedWithoutPersisting()
    {
        var (proxy, settings) = await StartAsync();

        var updated = new Settings();
        updated.Audio.DeviceId = "does-not-exist";

        var ack = await proxy.UpdateSettingsAsync(updated);

        Assert.False(ack.Success);
        Assert.Contains("DeviceId", ack.Error);
        Assert.Equal("default", settings.Settings.Audio.DeviceId); // nothing persisted
    }

    [Fact]
    public async Task UpdateSettings_MissingModelPath_RejectedWithoutPersisting()
    {
        var (proxy, settings) = await StartAsync();

        var updated = new Settings();
        updated.Whisper.ModelPath = "/nonexistent/nope.bin";

        var ack = await proxy.UpdateSettingsAsync(updated);

        Assert.False(ack.Success);
        Assert.Contains("ModelPath", ack.Error);
        Assert.Equal(string.Empty, settings.Settings.Whisper.ModelPath);
    }

    [Fact]
    public async Task UpdateSettings_UnknownOutputMode_Rejected()
    {
        var (proxy, settings) = await StartAsync();

        var updated = new Settings();
        updated.Output.Mode = "fax";

        var ack = await proxy.UpdateSettingsAsync(updated);

        Assert.False(ack.Success);
        Assert.Contains("Output.Mode", ack.Error);
    }

    [Fact]
    public async Task UpdateSettings_PipelineRestartFails_RollsBackToPreviousSettings()
    {
        var settings = new InMemorySettingsService();
        var activation = Substitute.For<IActivationManagerService>();
        activation.IsListening.Returns(true);
        var calls = 0;
        activation.ToggleListeningAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls++;
            if (calls == 2) throw new InvalidOperationException("no model loaded"); // start toggle fails
            return Task.CompletedTask;
        });

        _host = new InProcessEngineHost(NullLogger<InProcessEngineHost>.Instance);
        await _host.StartAsync(_socketPath, services =>
        {
            services.RemoveAll<ISettingsService>();
            services.AddSingleton<ISettingsService>(settings);
            services.RemoveAll<IAudioCaptureService>();
            services.AddSingleton<IAudioCaptureService, FakeAudioCapture>();
            services.RemoveAll<IActivationManagerService>();
            services.AddSingleton(activation);
        });
        _proxy = new EngineProxy(socketPathOverride: _socketPath);
        await _proxy.ConnectAsync();

        var updated = new Settings();
        updated.Whisper.ModelPath = Path.GetTempFileName();

        var ack = await _proxy.UpdateSettingsAsync(updated);

        // Controller sees the start failure
        Assert.False(ack.Success);
        Assert.Contains("no model loaded", ack.Error);
        // Engine rolled back: previous settings restored and restarted (3 toggles:
        // stop, failed start, rollback start)
        Assert.Equal("default", settings.Settings.Audio.DeviceId);
        Assert.Equal(string.Empty, settings.Settings.Whisper.ModelPath);
        Assert.Equal(3, calls);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_proxy is not null)
        {
            try { await _proxy.DisconnectAsync(); } catch { }
            _proxy.Dispose();
            _proxy = null;
        }
        if (_host is not null)
        {
            try { await _host.DisposeAsync(); } catch { }
            _host = null;
        }
        try { File.Delete(_socketPath); } catch { }
    }
}
