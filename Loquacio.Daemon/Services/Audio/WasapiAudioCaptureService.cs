using NAudio.CoreAudioApi;
using NAudio.Wave;
using Loquacio.Infrastructure;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.Daemon.Services.Audio;

public sealed class WasapiAudioCaptureService : IAudioCaptureService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private WasapiCapture? _capture;
    private WaveFormat? _activeFormat;
    private readonly MemoryStream _buffer = new();
    private DateTime _lastAudioTime;
    private DateTime _speechStartTime;
    private double _silenceThresholdDb = -40;
    private double _gain = 1.0;
    private bool _compressorEnabled = true;
    private double _compressorThresholdDb = -18;
    private double _compressorRatio = 4.0;
    private double _silenceThresholdMs = 1500;
    private bool _speechActive;
    private bool _flushInProgress;
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler<AudioSegment>? OnSegmentCaptured;
    public event EventHandler<double>? OnAudioLevel;
    public event EventHandler<Exception>? OnError;

    public bool IsCapturing => _capture is not null && _capture.CaptureState != CaptureState.Stopped;

    public IReadOnlyList<AudioDevice> GetAvailableDevices()
    {
        var devices = new List<AudioDevice>();
        var enumerator = new MMDeviceEnumerator();

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new AudioDevice
            {
                Id = device.ID,
                FriendlyName = device.FriendlyName,
                IsCaptureDevice = device.DataFlow == DataFlow.Capture
            });
        }

        return devices;
    }

    public void StartCapture(AudioDevice device, double silenceThresholdMs = 1500, double silenceThresholdDb = -40, double gain = 1.0,
        bool compressorEnabled = true, double compressorThresholdDb = -18, double compressorRatio = 4.0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopCapture();

        var enumerator = new MMDeviceEnumerator();
        var mmDevice = enumerator.GetDevice(device.Id);

        _capture = new WasapiCapture(mmDevice)
        {
            // 16-bit is universally supported in WASAPI shared mode.
            // 24-bit requests in shared mode may silently produce different formats.
            WaveFormat = new WaveFormat(16000, 16, 1) // 16kHz, 16-bit, mono — Whisper.net native format
        };

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        _silenceThresholdDb = Math.Clamp(silenceThresholdDb, -96, 0);
        _gain = Math.Clamp(gain, 0.1, 5.0);
        _compressorEnabled = compressorEnabled;
        _compressorThresholdDb = compressorThresholdDb;
        _compressorRatio = compressorRatio;
        _silenceThresholdMs = Math.Clamp(silenceThresholdMs, 250, 5000);
        _speechActive = false;
        _flushInProgress = false;
        _activeFormat = _capture.WaveFormat;
        _lastAudioTime = DateTime.UtcNow;
        _capture.StartRecording();

        Task.Run(() => SilenceDetectionLoop(_silenceThresholdMs), _cts.Token);
    }

    public void StopCapture()
    {
        // Detach the capture under the lock, then stop/dispose it OUTSIDE the lock.
        // WasapiCapture.Dispose() waits for the capture thread to drain, and that
        // thread fires OnDataAvailable which needs _lock — holding _lock while
        // disposing deadlocks (the "Cycle Mode hangs everything" bug).
        WasapiCapture? capture;
        lock (_lock)
        {
            capture = _capture;
            _capture = null;
        }

        if (capture is null) return;

        try
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.StopRecording();
        }
        catch (Exception)
        {
            // Capture may already be stopping/stopped
        }
        finally
        {
            try { capture.Dispose(); } catch { }
        }

        // Flush whatever is left in the buffer as a final segment
        _ = FlushBufferAsync();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        double level;
        bool shouldFlush = false;
        lock (_lock)
        {
            if (_disposed) return;

            // Calculate audio level (RMS) — dynamically detect bits per sample
            var format = _capture?.WaveFormat ?? _activeFormat;
            if (format is null) return;
            var audioBuffer = ApplyGain(e.Buffer, e.BytesRecorded, format, _gain);
            if (_compressorEnabled && format.BitsPerSample == 16)
                AudioCompressor.Process16BitPcm(audioBuffer, _compressorThresholdDb, _compressorRatio);
            int bytesPerSample = format.BitsPerSample / 8;
            if (bytesPerSample <= 0) return;
            int channels = format.Channels;
            if (channels <= 0) channels = 1;
            int sampleCount = e.BytesRecorded / (bytesPerSample * channels);
            double sum = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                int offset = i * bytesPerSample * channels;
                byte[] buf = audioBuffer;
                double normalized;

                if (bytesPerSample == 2) // 16-bit
                {
                    short sample16 = (short)(buf[offset] | (buf[offset + 1] << 8));
                    normalized = sample16 / 32768.0;
                }
                else if (bytesPerSample == 3) // 24-bit
                {
                    int lowByte = buf[offset];
                    int midByte = buf[offset + 1];
                    int highByte = buf[offset + 2];
                    int sample24 = (highByte << 16) | (midByte << 8) | lowByte;
                    if ((sample24 & 0x800000) != 0)
                        sample24 |= unchecked((int)0xFF000000);
                    normalized = sample24 / 8388607.0;
                }
                else if (bytesPerSample == 4) // 32-bit float
                {
                    normalized = BitConverter.ToSingle(buf, offset);
                }
                else
                {
                    normalized = 0;
                }
                sum += normalized * normalized;
            }
            double rms = sampleCount > 0 ? Math.Sqrt(sum / sampleCount) : 0;
            level = Math.Min(1.0, rms);
            var levelDb = level > 0 ? 20 * Math.Log10(level) : -96;
            if (levelDb >= _silenceThresholdDb)
            {
                if (!_speechActive)
                {
                    _speechActive = true;
                    _speechStartTime = DateTime.UtcNow;
                }
                _lastAudioTime = DateTime.UtcNow;
                _buffer.Write(audioBuffer, 0, audioBuffer.Length);
            }
            else if (_speechActive)
            {
                _buffer.Write(audioBuffer, 0, audioBuffer.Length);
                shouldFlush = (DateTime.UtcNow - _lastAudioTime).TotalMilliseconds >= _silenceThresholdMs;
            }
        }

        // Raise events OUTSIDE the lock: a slow/blocked subscriber (e.g. a stalled
        // VU-meter socket) must never stall the WASAPI capture thread while it
        // holds the buffer lock — same deadlock family as the Cycle Mode bug.
        OnAudioLevel?.Invoke(this, level);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            OnError?.Invoke(this, e.Exception);
        }

        _ = FlushSpeechAsync(force: true);
    }

    private async Task SilenceDetectionLoop(double silenceThresholdMs)
    {
        while (!_cts.IsCancellationRequested && IsCapturing)
        {
            await Task.Delay(100, _cts.Token);
            bool shouldFlush;
            lock (_lock)
            {
                shouldFlush = _speechActive &&
                    (DateTime.UtcNow - _lastAudioTime).TotalMilliseconds >= silenceThresholdMs;
            }
            if (shouldFlush)
                await FlushSpeechAsync();
        }
    }

    private async Task FlushBufferAsync()
    {
        byte[] data;
        lock (_lock)
        {
            if (_buffer.Length == 0) return;

            data = _buffer.ToArray();
            _buffer.SetLength(0);
        }

        var format = _activeFormat ?? new WaveFormat(16000, 16, 1);
        var segment = new AudioSegment
        {
            Data = data,
            SampleRate = format.SampleRate,
            Channels = format.Channels,
            BitsPerSample = format.BitsPerSample,
            Duration = TimeSpan.FromSeconds(data.Length / (double)(format.SampleRate * (format.BitsPerSample / 8) * format.Channels)),
            Timestamp = DateTime.UtcNow
        };

        try
        {
            await AudioChannel.WriteAsync(segment, _cts.Token);
            OnSegmentCaptured?.Invoke(this, segment);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
        }
        finally
        {
            lock (_lock)
                _flushInProgress = false;
        }
    }

    private static byte[] ApplyGain(byte[] input, int length, WaveFormat format, double gain)
    {
        var output = input[..length].ToArray();
        if (gain == 1.0) return output;

        if (format.BitsPerSample == 16)
        {
            for (var offset = 0; offset + 1 < output.Length; offset += 2)
            {
                var sample = BitConverter.ToInt16(output, offset);
                var amplified = Math.Clamp(sample * gain, short.MinValue, short.MaxValue);
                BitConverter.TryWriteBytes(output.AsSpan(offset, 2), (short)amplified);
            }
        }
        else if (format.BitsPerSample == 32 && format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (var offset = 0; offset + 3 < output.Length; offset += 4)
            {
                var amplified = Math.Clamp(BitConverter.ToSingle(output, offset) * gain, -1f, 1f);
                BitConverter.TryWriteBytes(output.AsSpan(offset, 4), amplified);
            }
        }

        return output;
    }

    private Task FlushSpeechAsync(bool force = false)
    {
        lock (_lock)
        {
            if (_flushInProgress || !_speechActive || _buffer.Length == 0)
                return Task.CompletedTask;

            if (!force && (DateTime.UtcNow - _speechStartTime).TotalMilliseconds < 200)
            {
                _buffer.SetLength(0);
                _speechActive = false;
                return Task.CompletedTask;
            }

            _flushInProgress = true;
            _speechActive = false;
        }

        return FlushBufferAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopCapture();
        _cts.Cancel();
        _cts.Dispose();
        lock (_lock)
        {
            _buffer.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
