namespace WhisperDictation.Models;

/// <summary>
/// Represents a single history entry for processed dictation segments.
/// </summary>
public sealed class HistoryEntry
{
    /// <summary>
    /// Unique identifier for the history entry.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Timestamp when the dictation was captured.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The raw Whisper transcription text.
    /// </summary>
    public required string RawText { get; set; }

    /// <summary>
    /// The corrected text after LLM post-processing (if enabled).
    /// </summary>
    public required string CorrectedText { get; set; }

    /// <summary>
    /// The Whisper model used for transcription.
    /// </summary>
    public required string Model { get; set; }

    /// <summary>
    /// The language code (e.g., "en", "es").
    /// </summary>
    public required string Language { get; set; }

    /// <summary>
    /// Duration of the audio segment in milliseconds.
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Whether LLM post-processing was applied.
    /// </summary>
    public bool LlmProcessed { get; set; }

    /// <summary>
    /// Optional error message if processing failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The text to display in history views (prioritizes corrected text).
    /// </summary>
    public string DisplayText => string.IsNullOrEmpty(CorrectedText) ? RawText : CorrectedText;
}