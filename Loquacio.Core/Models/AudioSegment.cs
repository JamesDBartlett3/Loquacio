namespace Loquacio.Models;

/// <summary>
/// Represents an audio segment captured from the microphone
/// </summary>
public class AudioSegment
{
    /// <summary>
    /// Raw PCM audio data (16-bit, 16kHz, mono — Whisper.net native format)
    /// </summary>
    public byte[]? Data { get; set; }

    /// <summary>
    /// Sample rate in Hz
    /// </summary>
    public int SampleRate { get; set; }

    /// <summary>
    /// Number of audio channels (1 = mono, 2 = stereo)
    /// </summary>
    public int Channels { get; set; }

    /// <summary>
    /// Bits per sample (16, 24, or 32)
    /// </summary>
    public int BitsPerSample { get; set; }

    /// <summary>
    /// Duration of the audio segment
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Timestamp when the segment was captured
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}