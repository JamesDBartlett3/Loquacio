using Loquacio.Models;

namespace Loquacio.Services;

/// <summary>
/// Service for keyword/wake-word detection to toggle dictation hands-free
/// </summary>
public interface IKeywordDetectionService : IDisposable
{
    /// <summary>
    /// Event fired when the keyword is detected
    /// </summary>
    event EventHandler? KeywordDetected;

    /// <summary>
    /// Event fired when an error occurs
    /// </summary>
    event EventHandler<Exception>? OnError;

    /// <summary>
    /// Whether keyword detection is currently active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Start listening for the configured keyword
    /// </summary>
    /// <param name="keyword">The keyword/phrase to detect</param>
    /// <param name="ct">Cancellation token</param>
    Task StartAsync(string keyword, CancellationToken ct = default);

    /// <summary>
    /// Stop keyword detection
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Process a chunk of audio data for keyword detection
    /// </summary>
    /// <param name="audioData">Raw audio samples (16kHz, 16-bit PCM)</param>
    void ProcessAudioFrame(short[] audioFrame);

    /// <summary>
    /// Check if keyword detection is available (requires model/config)
    /// </summary>
    bool IsAvailable { get; }
}


/// <summary>
/// Fallback keyword detection using energy-based voice activity detection.
/// Used when no Picovoice AccessKey is configured; any speech burst above the
/// energy threshold for the minimum frame count toggles activation. For
/// production-grade wake-word detection use <see cref="PorcupineKeywordDetectionService"/>.
/// </summary>
public class KeywordDetectionService : IKeywordDetectionService
{
    private readonly ILogger<KeywordDetectionService> _logger;
    private readonly IAudioCaptureService _audioCapture;
    private CancellationTokenSource? _cts;
    private bool _isActive;
    private bool _disposed;
    private string _keyword = "Hey Dictate";

    // Voice Activity Detection parameters
    private const int FrameSize = 512; // ~32ms at 16kHz
    private const double EnergyThreshold = 300.0;
    private const int MinConsecutiveFrames = 3;
    private const double CooldownMs = 3000; // Min time between detections

    private DateTime _lastDetection = DateTime.MinValue;
    private int _consecutiveActiveFrames;
    private bool _wasInSpeech;

    /// <summary>
    /// Event fired when the keyword is detected.
    /// </summary>
    public event EventHandler? KeywordDetected;

    /// <summary>
    /// Event fired when an error occurs. Currently unused in the energy-based
    /// implementation but required by the interface.
    /// </summary>
#pragma warning disable CS0067 // Event is part of the interface contract
    public event EventHandler<Exception>? OnError;
#pragma warning restore CS0067

    public bool IsActive => _isActive;

    /// <summary>
    /// Keyword detection is always "available" with the energy-based fallback.
    /// </summary>
    public bool IsAvailable => true;

    public KeywordDetectionService(
        IAudioCaptureService audioCapture,
        ILogger<KeywordDetectionService> logger)
    {
        _audioCapture = audioCapture;
        _logger = logger;
    }

    public Task StartAsync(string keyword, CancellationToken ct = default)
    {
        if (_isActive)
        {
            _logger.LogWarning("Keyword detection is already active");
            return Task.CompletedTask;
        }

        _keyword = keyword;
        _isActive = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Subscribe to audio level for VAD-based detection
        _audioCapture.OnAudioLevel += OnAudioLevel;

        _logger.LogInformation("Keyword detection started for '{Keyword}'", keyword);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_isActive)
            return Task.CompletedTask;

        _isActive = false;
        _audioCapture.OnAudioLevel -= OnAudioLevel;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _consecutiveActiveFrames = 0;
        _wasInSpeech = false;

        _logger.LogInformation("Keyword detection stopped");
        return Task.CompletedTask;
    }

    private void OnAudioLevel(object? sender, double level)
    {
        if (!_isActive) return;

        // Convert RMS level to approximate energy
        var energy = level * level * 10000;

        if (energy > EnergyThreshold)
        {
            _consecutiveActiveFrames++;
        }
        else
        {
            // End of speech burst
            if (_consecutiveActiveFrames >= MinConsecutiveFrames && _wasInSpeech)
            {
                TryDetectKeyword();
            }
            _consecutiveActiveFrames = 0;
        }

        _wasInSpeech = energy > EnergyThreshold;
    }

    /// <summary>
    /// Process a raw audio frame for keyword detection (energy-based VAD).
    /// </summary>
    public void ProcessAudioFrame(short[] audioFrame)
    {
        if (!_isActive || audioFrame is null || audioFrame.Length == 0) return;

        double sumSquares = 0;
        for (int i = 0; i < audioFrame.Length; i++)
        {
            sumSquares += (double)audioFrame[i] * audioFrame[i];
        }

        var rms = Math.Sqrt(sumSquares / audioFrame.Length);

        if (rms > EnergyThreshold)
        {
            _consecutiveActiveFrames++;
        }
        else if (_consecutiveActiveFrames >= MinConsecutiveFrames)
        {
            TryDetectKeyword();
            _consecutiveActiveFrames = 0;
        }
    }

    private void TryDetectKeyword()
    {
        var now = DateTime.UtcNow;

        // Enforce cooldown to prevent rapid re-triggering
        if ((now - _lastDetection).TotalMilliseconds < CooldownMs)
            return;

        _lastDetection = now;

        _logger.LogInformation("Keyword detected (energy-based VAD) for '{Keyword}'", _keyword);
        KeywordDetected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Minimal seam over the native Porcupine engine so tests can exercise the
/// service without a Picovoice AccessKey or the native audio runtime.
/// </summary>
public interface IPorcupineEngine : IDisposable
{
    /// <summary>Number of PCM samples expected per Process call.</summary>
    int FrameLength { get; }

    /// <summary>Required sample rate in Hz (Porcupine: 16 kHz).</summary>
    int SampleRate { get; }

    /// <summary>Process one frame; returns &gt;= 0 (keyword index) on detection, -1 otherwise.</summary>
    int Process(short[] pcm);
}

/// <summary>
/// Creates Porcupine engines from a Picovoice AccessKey plus either a custom
/// .ppn keyword model path or a built-in keyword name.
/// </summary>
public interface IPorcupineEngineFactory
{
    /// <summary>
    /// Create an engine. If <paramref name="keywordModelPath"/> points at an
    /// existing .ppn file it is used; otherwise <paramref name="keyword"/> must
    /// name a Porcupine built-in keyword (case/punctuation-insensitive, e.g.
    /// "Porcupine", "Jarvis", "Hey Siri").
    /// </summary>
    IPorcupineEngine Create(string accessKey, string? keywordModelPath, string keyword);
}

/// <summary>
/// Thin wrapper over the Picovoice Porcupine SDK (NuGet: Porcupine 4.x).
/// </summary>
public sealed class PorcupineEngineFactory : IPorcupineEngineFactory
{
    private sealed class Engine : IPorcupineEngine
    {
        private readonly Pv.Porcupine _porcupine;
        public Engine(Pv.Porcupine porcupine) => _porcupine = porcupine;
        public int FrameLength => _porcupine.FrameLength;
        public int SampleRate => _porcupine.SampleRate;
        public int Process(short[] pcm) => _porcupine.Process(pcm);
        public void Dispose() => _porcupine.Dispose();
    }

    public IPorcupineEngine Create(string accessKey, string? keywordModelPath, string keyword)
    {
        if (string.IsNullOrWhiteSpace(accessKey))
            throw new InvalidOperationException(
                "Picovoice AccessKey is required (set the PICOVOICE_ACCESS_KEY environment variable).");

        if (!string.IsNullOrWhiteSpace(keywordModelPath) && File.Exists(keywordModelPath))
        {
            return new Engine(Pv.Porcupine.FromKeywordPaths(accessKey, [keywordModelPath]));
        }

        var normalized = keyword.Trim().Replace(" ", "").Replace("_", "").Replace("-", "");
        var match = Enum.GetNames<Pv.BuiltInKeyword>()
            .FirstOrDefault(n => n.Replace("_", "").Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException(
                $"'{keyword}' is not a Porcupine built-in keyword. Train a custom wake word " +
                "(e.g. \"Hey Dictate\") at https://console.picovoice.ai/ and point " +
                "PICOVOICE_KEYWORD_PATH at the downloaded .ppn file.");

        return new Engine(Pv.Porcupine.FromBuiltInKeywords(accessKey, [Enum.Parse<Pv.BuiltInKeyword>(match)]));
    }
}

/// <summary>
/// Keyword detection using the Picovoice Porcupine wake-word engine.
///
/// Requires:
///   - PICOVOICE_ACCESS_KEY env var (free key from https://console.picovoice.ai/)
///   - either a built-in keyword name, or a custom .ppn model via
///     PICOVOICE_KEYWORD_PATH (the default "Hey Dictate" is custom).
///
/// Audio frames fed to <see cref="ProcessAudioFrame"/> are re-chunked to the
/// engine's frame length; engine errors are surfaced via <see cref="OnError"/>
/// without crashing the audio pipeline.
/// </summary>
public class PorcupineKeywordDetectionService : IKeywordDetectionService
{
    private const double CooldownMs = 3000; // Min time between detections

    private readonly ILogger<PorcupineKeywordDetectionService> _logger;
    private readonly IPorcupineEngineFactory _engineFactory;
    private readonly string? _keywordModelPath;

    private IPorcupineEngine? _engine;
    private readonly Queue<short> _sampleBuffer = new();
    private DateTime _lastDetection = DateTime.MinValue;
    private bool _isActive;
    private bool _disposed;

    public event EventHandler? KeywordDetected;
    public event EventHandler<Exception>? OnError;

    public bool IsActive => _isActive;

    /// <summary>
    /// Requires AccessKey and model file to be configured
    /// </summary>
    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY"));

    public PorcupineKeywordDetectionService(
        ILogger<PorcupineKeywordDetectionService> logger,
        IPorcupineEngineFactory? engineFactory = null)
    {
        _logger = logger;
        _engineFactory = engineFactory ?? new PorcupineEngineFactory();
        _keywordModelPath = Environment.GetEnvironmentVariable("PICOVOICE_KEYWORD_PATH");
    }

    public Task StartAsync(string keyword, CancellationToken ct = default)
    {
        if (_isActive)
        {
            _logger.LogWarning("Keyword detection is already active");
            return Task.CompletedTask;
        }

        if (!IsAvailable)
        {
            _logger.LogWarning(
                "Porcupine keyword detection unavailable — set PICOVOICE_ACCESS_KEY " +
                "(free key from https://console.picovoice.ai/)");
            return Task.CompletedTask;
        }

        var accessKey = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY")!;
        try
        {
            _engine = _engineFactory.Create(accessKey, _keywordModelPath, keyword);
            _sampleBuffer.Clear();
            _isActive = true;
            _logger.LogInformation(
                "Porcupine keyword detection started for '{Keyword}' (model: {Model})",
                keyword, string.IsNullOrWhiteSpace(_keywordModelPath) ? "built-in" : _keywordModelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Porcupine engine for '{Keyword}'", keyword);
            OnError?.Invoke(this, ex);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_isActive)
            return Task.CompletedTask;

        _isActive = false;
        _engine?.Dispose();
        _engine = null;
        _sampleBuffer.Clear();

        _logger.LogInformation("Porcupine keyword detection stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Feed raw PCM (16 kHz, mono, 16-bit) into the engine. Input chunks of any
    /// size are accepted; they are re-chunked to the engine frame length.
    /// </summary>
    public void ProcessAudioFrame(short[] audioFrame)
    {
        if (!_isActive || _engine is null || audioFrame is null || audioFrame.Length == 0)
            return;

        foreach (var sample in audioFrame)
            _sampleBuffer.Enqueue(sample);

        var frame = new short[_engine.FrameLength];
        while (_sampleBuffer.Count >= _engine.FrameLength)
        {
            for (var i = 0; i < frame.Length; i++)
                frame[i] = _sampleBuffer.Dequeue();

            try
            {
                var keywordIndex = _engine.Process(frame);
                if (keywordIndex >= 0)
                    TryDetect();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Porcupine Process failed; stopping keyword detection");
                OnError?.Invoke(this, ex);
                StopAsync().GetAwaiter().GetResult();
                return;
            }
        }
    }

    private void TryDetect()
    {
        var now = DateTime.UtcNow;

        // Enforce cooldown to prevent rapid re-triggering
        if ((now - _lastDetection).TotalMilliseconds < CooldownMs)
            return;

        _lastDetection = now;

        _logger.LogInformation("Wake word detected (Porcupine)");
        KeywordDetected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
}
}
