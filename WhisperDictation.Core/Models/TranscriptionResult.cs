namespace WhisperDictation.Models;

/// <summary>
/// Represents the result of a transcription
/// </summary>
public class TranscriptionResult
{
    /// <summary>
    /// Transcribed text
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the transcription was generated
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Duration of the audio segment
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Detected language (null if auto-detect)
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Whether post-processing (LLM, filler removal) was applied
    /// </summary>
    public bool IsPostProcessed { get; set; }

    /// <summary>
    /// Raw Whisper output (before post-processing)
    /// </summary>
    public string? RawText { get; set; }
}