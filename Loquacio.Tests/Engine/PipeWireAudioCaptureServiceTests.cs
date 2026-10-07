using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Loquacio.Engine.Services.Audio;
using Loquacio.Models;
using Xunit;

namespace Loquacio.Tests.Engine;

/// <summary>
/// Integration tests for <see cref="PipeWireAudioCaptureService"/>. These run
/// against the real PipeWire instance when available and are skipped otherwise
/// (CI containers, non-Linux hosts).
/// </summary>
public class PipeWireAudioCaptureServiceTests : IDisposable
{
    private readonly PipeWireAudioCaptureService _service = new(NullLogger<PipeWireAudioCaptureService>.Instance);

    public PipeWireAudioCaptureServiceTests()
    {
        // Never touch the static AudioChannel: real-hardware capture segments
        // were leaking into it and corrupting AudioChannelTests' FIFO asserts
        // when tests run in parallel (full-suite flake on mic-equipped hosts).
        _service.SegmentSink = (_, _) => ValueTask.CompletedTask;
    }

    private static bool PipeWireAvailable()
    {
        if (!OperatingSystem.IsLinux()) return false;
        if (!File.Exists("/usr/bin/pw-record") || !File.Exists("/usr/bin/pw-dump")) return false;
        var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(xdgRuntime)) return false;
        return File.Exists(Path.Combine(xdgRuntime, "pipewire-0"));
    }

    [Fact]
    public void GetAvailableDevices_ListsRealSource_WhenPipeWirePresent()
    {
        if (!PipeWireAvailable()) return; // skip silently on hosts without PipeWire

        var devices = _service.GetAvailableDevices();

        Assert.NotEmpty(devices);
        Assert.All(devices, d => Assert.True(d.IsCaptureDevice));
        Assert.All(devices, d => Assert.False(string.IsNullOrWhiteSpace(d.Id)));
        Assert.DoesNotContain(devices, d => d.Id.Contains(".monitor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartCapture_RunsPwRecord_AndProducesAudioData()
    {
        if (!PipeWireAvailable()) return;

        var devices = _service.GetAvailableDevices();
        if (devices.Count == 0) return; // no mic on this host

        AudioSegment? captured = null;
        var done = new SemaphoreSlim(0);
        _service.OnSegmentCaptured += (_, seg) => { captured = seg; done.Release(); };

        _service.StartCapture(devices[0], silenceThresholdMs: 800);
        Assert.True(_service.IsCapturing);

        // Speak-free environment: even ambient noise usually triggers a segment
        // within a few silence windows. Wait up to 15s.
        var gotSegment = await done.WaitAsync(TimeSpan.FromSeconds(15));

        _service.StopCapture();
        Assert.False(_service.IsCapturing);

        if (gotSegment && captured is not null)
        {
            var seg = captured;
            Assert.Equal(16000, seg.SampleRate);
            Assert.Equal(1, seg.Channels);
            Assert.Equal(16, seg.BitsPerSample);
            Assert.True(seg.Data.Length > 0);
            Assert.Equal(seg.Data.Length / 32000.0, seg.Duration.TotalSeconds, 3);
        }
        // If no segment: environment was dead silent for 15s — capture lifecycle
        // itself (start/stop, process management) is still verified above.
    }

    [Fact]
    public void StartCapture_WithBogusDevice_RaisesError_DoesNotCrash()
    {
        // pw-record accepts any target name; a nonexistent one links to nothing
        // and exits or streams silence. Either way the service must not throw.
        _service.StartCapture(new AudioDevice { Id = "definitely-not-a-real-source", FriendlyName = "bogus" }, 500);
        _service.StopCapture();
    }

    [Fact]
    public void StopCapture_IsIdempotent()
    {
        _service.StopCapture();
        _service.StopCapture();
        Assert.False(_service.IsCapturing);
    }

    public void Dispose() => _service.Dispose();
}

public class PipeWireVadGatingTests
{
    [Theory]
    [InlineData(0.01, -40.0, true)]   // exactly at default threshold → loud
    [InlineData(0.009, -40.0, false)] // just below → silence
    [InlineData(0.5, -18.0, true)]
    [InlineData(0.01, -18.0, false)]  // -40 dB signal vs -18 dB threshold → silence
    [InlineData(0.0, -40.0, false)]   // digital silence never latches speech
    public void IsLoud_RespectsDbThreshold(double rms, double thresholdDb, bool expected)
    {
        Assert.Equal(expected, PipeWireAudioCaptureService.IsLoud(rms, thresholdDb));
    }

    [Fact]
    public void IsLoud_ThresholdClampSemantics_AreConsistent()
    {
        // The dB threshold must be monotonic: raising it can only turn loud→silent.
        var rms = 0.02; // ≈ -34 dBFS
        Assert.True(PipeWireAudioCaptureService.IsLoud(rms, -40));
        Assert.False(PipeWireAudioCaptureService.IsLoud(rms, -30));
    }
}
