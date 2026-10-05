using Microsoft.Extensions.Logging;
using Whisper.net;
using WhisperDictation.Infrastructure;
using WhisperDictation.Models;

namespace WhisperDictation.Services;

/// <summary>
/// Whisper speech recognition processor using Whisper.net
/// </summary>
public class WhisperProcessorService : IWhisperProcessorService, IDisposable
{
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private readonly ILogger<WhisperProcessorService> _logger;
    private bool _disposed;
    private readonly object _lock = new();

    public bool IsModelLoaded => _processor is not null;

    public WhisperProcessorService(ILogger<WhisperProcessorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Load a Whisper model from the specified path
    /// </summary>
    public void LoadModel(string modelPath, string language = "auto")
    {
        lock (_lock)
        {
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Whisper model not found: {modelPath}");
            }

            _logger.LogInformation("Loading Whisper model from {Path} (language: {Language})", modelPath, language);

            // Dispose previous processor if exists
            _processor?.Dispose();
            _factory?.Dispose();

            // Create factory with model path
            _factory = WhisperFactory.FromPath(modelPath);

            // Create processor builder with configured language
            _processor = _factory.CreateBuilder()
                .WithLanguage(string.IsNullOrWhiteSpace(language) ? "auto" : language)
                .Build();
            _logger.LogInformation("Whisper model loaded successfully");
        }
    }

    /// <summary>
    /// Process an audio segment and return transcription
    /// </summary>
    public async Task<string> ProcessAsync(AudioSegment segment, CancellationToken ct = default)
    {
        if (_processor is null)
        {
            throw new InvalidOperationException("Whisper model not loaded. Call LoadModel first.");
        }

        if (segment.Data is null || segment.Data.Length == 0)
        {
            _logger.LogWarning("Received empty audio segment");
            return string.Empty;
        }

        _logger.LogDebug("Processing audio segment: {Duration}s, {Bytes} bytes",
            segment.Duration.TotalSeconds, segment.Data.Length);

        // Convert PCM bytes to float samples and resample to 16kHz for Whisper.net
        var samples = ConvertToFloatSamples(segment.Data, segment.BitsPerSample, segment.SampleRate);

        // Process with Whisper
        var transcription = new List<string>();

        await foreach (var result in _processor.ProcessAsync(samples, ct))
        {
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                transcription.Add(result.Text.Trim());
                _logger.LogDebug("Transcription segment: {Text}", result.Text);
            }
        }

        var fullTranscription = string.Join(" ", transcription);
        _logger.LogInformation("Transcription complete: {Length} characters", fullTranscription.Length);

        return fullTranscription;
    }

    /// <summary>
    /// Convert PCM byte data to float samples normalized to [-1.0, 1.0].
    /// Handles 16-bit and 24-bit PCM input.
    /// Resamples to 16 kHz mono (Whisper.net requirement).
    /// </summary>
    private static float[] ConvertToFloatSamples(byte[] input, int bitsPerSample, int sourceSampleRate)
    {
        int bytesPerSample = bitsPerSample / 8;
        int sampleCount = input.Length / bytesPerSample;
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            int offset = i * bytesPerSample;
            double normalized;

            if (bitsPerSample == 24)
            {
                int lowByte = input[offset];
                int midByte = input[offset + 1];
                int highByte = input[offset + 2];
                int sample24 = (highByte << 16) | (midByte << 8) | lowByte;
                if ((sample24 & 0x800000) != 0)
                    sample24 |= unchecked((int)0xFF000000);
                normalized = sample24 / 8388607.0; // 2^23 - 1
            }
            else if (bitsPerSample == 16)
            {
                short sample16 = (short)(input[offset] | (input[offset + 1] << 8));
                normalized = sample16 / 32768.0; // 2^15
            }
            else
            {
                throw new NotSupportedException($"Unsupported bits per sample: {bitsPerSample}");
            }

            samples[i] = (float)normalized;
        }

        // Resample to 16 kHz if needed (Whisper.net requires 16 kHz mono float samples)
        if (sourceSampleRate != 16000)
        {
            samples = ResampleLinear(samples, sourceSampleRate, 16000);
        }

        return samples;
    }

    /// <summary>
    /// Simple linear interpolation resampling.
    /// Good enough for speech recognition quality.
    /// </summary>
    private static float[] ResampleLinear(float[] input, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate)
            return input;

        double ratio = (double)sourceRate / targetRate;
        int outputLength = (int)(input.Length / ratio);
        float[] output = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double srcPos = i * ratio;
            int srcIndex = (int)srcPos;
            double frac = srcPos - srcIndex;

            if (srcIndex + 1 < input.Length)
            {
                output[i] = (float)(input[srcIndex] * (1.0 - frac) + input[srcIndex + 1] * frac);
            }
            else
            {
                output[i] = input[Math.Min(srcIndex, input.Length - 1)];
            }
        }

        return output;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            _processor?.Dispose();
            _factory?.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}