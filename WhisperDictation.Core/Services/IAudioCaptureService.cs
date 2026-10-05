using WhisperDictation.Models;

namespace WhisperDictation.Services;

public interface IAudioCaptureService
{
    event EventHandler<AudioSegment>? OnSegmentCaptured;
    event EventHandler<double>? OnAudioLevel;
    event EventHandler<Exception>? OnError;

    IReadOnlyList<AudioDevice> GetAvailableDevices();
    void StartCapture(AudioDevice device, double silenceThresholdMs = 1500, double silenceThresholdDb = -40, double gain = 1.0,
        bool compressorEnabled = true, double compressorThresholdDb = -18, double compressorRatio = 4.0);
    void StopCapture();
    bool IsCapturing { get; }
}