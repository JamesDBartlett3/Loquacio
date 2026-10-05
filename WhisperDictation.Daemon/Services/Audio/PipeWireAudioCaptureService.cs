using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WhisperDictation.Infrastructure;
using WhisperDictation.Models;
using WhisperDictation.Services;

namespace WhisperDictation.Daemon.Services.Audio;

/// <summary>
/// Linux audio capture via PipeWire's <c>pw-record</c> tool (works against both
/// PipeWire and PipeWire's PulseAudio compatibility layer). Captures 16kHz
/// 16-bit mono PCM — the Whisper.net native format — into a subprocess pipe
/// and applies level-based VAD segmentation: when the signal stays below the
/// silence threshold for <paramref name="silenceThresholdMs"/>, the buffered
/// audio is flushed as an <see cref="AudioSegment"/>.
///
/// Unlike the WASAPI implementation (which segments on data gaps), PipeWire
/// delivers a continuous stream even during silence, so segmentation is driven
/// by measured RMS level, not by absence of data.
/// </summary>
public sealed class PipeWireAudioCaptureService(ILogger<PipeWireAudioCaptureService> logger) : IAudioCaptureService, IDisposable
{
    /// <summary>
    /// Test hook: redirects flushed segments away from the static <see cref="AudioChannel"/>.
    /// The PipeWire integration tests run against real hardware and would otherwise
    /// pollute the shared channel that AudioChannelTests assert against (PR #7
    /// follow-up #3: full-suite flake on hosts with a microphone).
    /// </summary>
    internal Func<AudioSegment, CancellationToken, ValueTask> SegmentSink { get; set; } =
        AudioChannel.WriteAsync;

    private ValueTask WriteSegmentAsync(AudioSegment segment, CancellationToken ct) =>
        SegmentSink(segment, ct);

    private const string RecordTool = "pw-record";
    private const int Rate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    /// <summary>RMS level below which audio counts as silence (0-1).</summary>
    private const double SilenceRmsThreshold = 0.01;

    /// <summary>
    /// True when the measured RMS level meets or exceeds the silence threshold
    /// (expressed in dBFS). Kept internal + pure so VAD gating is unit-testable
    /// without a live PipeWire instance.
    /// </summary>
    internal static bool IsLoud(double rms, double thresholdDb)
        => rms > 0 && 20 * Math.Log10(rms) >= thresholdDb;

    private readonly MemoryStream _buffer = new();
    private DateTime _lastLoudTime;
    private DateTime _speechStartTime;
    private double _silenceThresholdMs = 1500;
    private double _silenceThresholdDb = -40;
    private double _gain = 1.0;
    private bool _compressorEnabled = true;
    private double _compressorThresholdDb = -18;
    private double _compressorRatio = 4.0;
    private bool _speechActive;
    private bool _flushInProgress;
    private readonly object _lock = new();
    private Process? _process;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public event EventHandler<AudioSegment>? OnSegmentCaptured;
    public event EventHandler<double>? OnAudioLevel;
    public event EventHandler<Exception>? OnError;

    public bool IsCapturing
    {
        get
        {
            var p = _process;
            return p is not null && !p.HasExited;
        }
    }

    public IReadOnlyList<AudioDevice> GetAvailableDevices()
    {
        var devices = new List<AudioDevice>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pw-dump",
                Arguments = "Node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var dump = Process.Start(psi);
            if (dump is null) return devices;
            var json = dump.StandardOutput.ReadToEnd();
            dump.WaitForExit(5000);

            // Minimal JSON scan for source nodes (avoid dragging in a JSON parser
            // for nested content we don't need — props are flat per node).
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var node in doc.RootElement.EnumerateArray())
            {
                if (!node.TryGetProperty("info", out var info)) continue;
                if (!info.TryGetProperty("props", out var props)) continue;
                if (!props.TryGetProperty("media.class", out var mediaClass)) continue;
                var mc = mediaClass.GetString();
                if (mc != "Audio/Source") continue; // skip sinks and monitors
                if (!props.TryGetProperty("node.name", out var name)) continue;

                var nodeName = name.GetString() ?? "";
                devices.Add(new AudioDevice
                {
                    Id = nodeName,
                    FriendlyName = props.TryGetProperty("node.description", out var desc)
                        ? desc.GetString() ?? nodeName
                        : nodeName,
                    IsCaptureDevice = true,
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enumerate PipeWire sources via pw-dump");
        }
        return devices;
    }

    public void StartCapture(AudioDevice device, double silenceThresholdMs = 1500, double silenceThresholdDb = -40, double gain = 1.0,
        bool compressorEnabled = true, double compressorThresholdDb = -18, double compressorRatio = 4.0)
    {
        StopCapture();
        _silenceThresholdMs = Math.Clamp(silenceThresholdMs, 250, 5000);
        _silenceThresholdDb = Math.Clamp(silenceThresholdDb, -96, 0);
        _gain = Math.Clamp(gain, 0.1, 5.0);
        _compressorEnabled = compressorEnabled;
        _compressorThresholdDb = compressorThresholdDb;
        _compressorRatio = compressorRatio;
        _speechActive = false;
        _flushInProgress = false;
        _cts = new CancellationTokenSource();

        var psi = new ProcessStartInfo
        {
            FileName = RecordTool,
            Arguments = $"--rate {Rate} --channels {Channels} --format s16 --target \"{device.Id}\" -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        try
        {
            var proc = Process.Start(psi) ?? throw new InvalidOperationException("pw-record failed to start");
            _process = proc;
            _speechStartTime = DateTime.UtcNow;
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    logger.LogDebug("pw-record: {Line}", e.Data);
            };
            proc.BeginErrorReadLine();

            _ = Task.Run(() => ReadPcmLoopAsync(proc, _cts.Token), _cts.Token);
            _ = Task.Run(() => SilenceDetectionLoopAsync(_cts.Token), _cts.Token);
            logger.LogInformation("PipeWire capture started on {Device} (16kHz/16-bit/mono)", device.Id);
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            logger.LogError(ex, "Failed to start pw-record. Is PipeWire running?");
        }
    }

    public void StopCapture()
    {
        lock (_lock)
        {
            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                        _process.Kill(entireProcessTree: true);
                }
                catch { /* already gone */ }
                _process.Dispose();
                _process = null;
            }
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task ReadPcmLoopAsync(Process proc, CancellationToken ct)
    {
        var stream = proc.StandardOutput.BaseStream;
        var chunk = new byte[3200]; // 100ms of 16kHz 16-bit mono
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct);
                if (read <= 0) break; // pw-record exited

                var audioBuffer = ApplyGain(chunk, read, _gain);
                if (_compressorEnabled)
                    AudioCompressor.Process16BitPcm(audioBuffer, _compressorThresholdDb, _compressorRatio);
                double rms = ComputeRms(audioBuffer, read);

                lock (_lock)
                {
                    _buffer.Write(audioBuffer, 0, read);
                    if (IsLoud(rms, _silenceThresholdDb))
                    {
                        if (!_speechActive)
                        {
                            _speechActive = true;
                            _speechStartTime = DateTime.UtcNow;
                        }
                        _lastLoudTime = DateTime.UtcNow;
                    }
                }

                OnAudioLevel?.Invoke(this, rms);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
        }
    }

    private async Task SilenceDetectionLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);
                bool shouldFlush = false;
                lock (_lock)
                {
                    shouldFlush = _speechActive
                        && (DateTime.UtcNow - _lastLoudTime).TotalMilliseconds >= _silenceThresholdMs;
                }
                if (shouldFlush)
                    await FlushSpeechAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    private async Task FlushBufferAsync(CancellationToken ct)
    {
        byte[] data;
        lock (_lock)
        {
            if (_buffer.Length == 0) return;
            data = _buffer.ToArray();
            _buffer.SetLength(0);
        }

        var segment = new AudioSegment
        {
            Data = data,
            SampleRate = Rate,
            Channels = Channels,
            BitsPerSample = BitsPerSample,
            Duration = TimeSpan.FromSeconds(data.Length / (double)(Rate * 2)),
            Timestamp = DateTime.UtcNow,
        };

        try
        {
            await WriteSegmentAsync(segment, ct);
            OnSegmentCaptured?.Invoke(this, segment);
        }
        catch (OperationCanceledException) { /* shutdown */ }
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

    private static double ComputeRms(ReadOnlySpan<byte> buf, int length)
    {
        int sampleCount = length / 2;
        if (sampleCount == 0) return 0;
        double sum = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(buf[i * 2] | (buf[i * 2 + 1] << 8));
            double n = s / 32768.0;
            sum += n * n;
        }
        return Math.Sqrt(sum / sampleCount);
    }

    private static byte[] ApplyGain(byte[] input, int length, double gain)
    {
        var output = input[..length].ToArray();
        if (gain == 1.0) return output;

        for (var offset = 0; offset + 1 < output.Length; offset += 2)
        {
            var sample = BitConverter.ToInt16(output, offset);
            var amplified = Math.Clamp(sample * gain, short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(output.AsSpan(offset, 2), (short)amplified);
        }

        return output;
    }

    private Task FlushSpeechAsync(CancellationToken ct, bool force = false)
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

        return FlushBufferAsync(ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCapture();
        _buffer.Dispose();
        GC.SuppressFinalize(this);
    }
}
