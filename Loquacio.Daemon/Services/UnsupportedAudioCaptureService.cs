using Microsoft.Extensions.Logging;
using Loquacio.Models;

namespace Loquacio.Daemon.Services;

/// <summary>
/// Fallback <see cref="IAudioCaptureService"/> for platforms without a native
/// audio capture implementation (currently macOS in the daemon).
///
/// WASAPI capture (Windows) is provided by
/// <see cref="Audio.WasapiAudioCaptureService"/>; PipeWire capture (Linux) by
/// <see cref="Audio.PipeWireAudioCaptureService"/>. macOS capture is planned
/// for Phase D. This class keeps the DI graph resolvable on all platforms and
/// reports a clear error instead of crashing the daemon.
/// </summary>
public sealed class UnsupportedAudioCaptureService(ILogger<UnsupportedAudioCaptureService> logger) : IAudioCaptureService, IDisposable
{
#pragma warning disable CS0067 // events unused in this fallback implementation
    public event EventHandler<AudioSegment>? OnSegmentCaptured;
    public event EventHandler<double>? OnAudioLevel;
    public event EventHandler<Exception>? OnError;
#pragma warning restore CS0067

    public bool IsCapturing => false;

    public IReadOnlyList<AudioDevice> GetAvailableDevices() => [];

    public void StartCapture(AudioDevice device, double silenceThresholdMs = 1500, double silenceThresholdDb = -40, double gain = 1.0,
        bool compressorEnabled = true, double compressorThresholdDb = -18, double compressorRatio = 4.0)
    {
        logger.LogError(
            "Cannot start audio capture on {Platform}: not implemented. " +
            "Windows uses WASAPI (WasapiAudioCaptureService); Linux PipeWire/PulseAudio support is planned (Phase D).",
            Environment.OSVersion.Platform);
        OnError?.Invoke(this, new PlatformNotSupportedException(
            $"Audio capture is not implemented on {Environment.OSVersion.Platform}."));
    }

    public void StopCapture() { }

    public void Dispose() { }
}
