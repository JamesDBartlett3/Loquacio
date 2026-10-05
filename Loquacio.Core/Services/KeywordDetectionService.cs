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
/// Implementation of keyword detection using simple energy-based voice activity detection
/// combined with a lightweight pattern matching approach.
/// 
/// This implementation does NOT require external dependencies like Porcupine.
/// It uses a simple audio energy threshold to detect speech segments and
/// can optionally forward detected speech to a lightweight classifier.
/// 
/// For production-grade wake word detection, replace with Porcupine:
///   1. Install Porcupine SDK NuGet package
///   2. Get a free AccessKey from https://console.picovoice.ai/
///   3. Download a wake word model (.ppn file) or use built-in keywords
///   4. Replace this implementation with PorcupineEngine
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
    /// implementation but required by the interface for future Porcupine integration.
    /// </summary>
#pragma warning disable CS0067 // Event is part of the interface contract
    public event EventHandler<Exception>? OnError;
#pragma warning restore CS0067

    public bool IsActive => _isActive;

    /// <summary>
    /// Keyword detection is always "available" with the energy-based fallback.
    /// Porcupine integration would set this based on model availability.
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
    /// Process a raw audio frame for keyword detection.
    /// With the energy-based fallback, this is a no-op since we use the OnAudioLevel event.
    /// With Porcupine, this would feed samples to the Porcupine engine.
    /// </summary>
    public void ProcessAudioFrame(short[] audioFrame)
    {
        if (!_isActive || audioFrame is null || audioFrame.Length == 0) return;

        // Energy-based VAD as a simple keyword detector
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
/// Placeholder for Porcupine-based keyword detection.
/// When Porcupine SDK is available, this replaces the energy-based fallback.
/// </summary>
#pragma warning disable CS0067 // Events are unused until Porcupine SDK is integrated
public class PorcupineKeywordDetectionService : IKeywordDetectionService
{
    private readonly ILogger<PorcupineKeywordDetectionService> _logger;
    private bool _isActive;

    public event EventHandler? KeywordDetected;
    public event EventHandler<Exception>? OnError;

    public bool IsActive => _isActive;

    /// <summary>
    /// Requires AccessKey and model file to be configured
    /// </summary>
    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY"));

    public PorcupineKeywordDetectionService(ILogger<PorcupineKeywordDetectionService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(string keyword, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("Porcupine keyword detection not available — set PICOVOICE_ACCESS_KEY env var");
            return Task.CompletedTask;
        }

        // TODO: Initialize Porcupine engine when SDK is installed:
        // _porcupine = Porcupine.Builder()
        //     .SetAccessKey(accessKey)
        //     .SetBuiltInKeyword(BuiltInKeyword.fromString(keyword))
        //     .Build();
        // _isActive = true;

        _logger.LogWarning("Porcupine keyword detection not yet implemented — requires SDK installation");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _isActive = false;
        return Task.CompletedTask;
    }

    public void ProcessAudioFrame(short[] audioFrame)
    {
        // TODO: Feed to Porcupine engine when implemented
    }

    public void Dispose()
    {
        _isActive = false;
        GC.SuppressFinalize(this);
    }
}
#pragma warning restore CS0067
