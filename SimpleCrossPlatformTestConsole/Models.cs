namespace SimpleCrossPlatformTestConsole;

/// <summary>
/// Application settings
/// </summary>
public class Settings
{
    /// <summary>
    /// Audio settings
    /// </summary>
    public AudioSettings Audio { get; set; } = new();

    /// <summary>
    /// Whisper settings
    /// </summary>
    public WhisperSettings Whisper { get; set; } = new();

    /// <summary>
    /// LLM settings
    /// </summary>
    public LLMSettings LLM { get; set; } = new();

    /// <summary>
    /// Activation settings
    /// </summary>
    public ActivationSettings Activation { get; set; } = new();

    /// <summary>
    /// Vocabulary settings
    /// </summary>
    public VocabularySettings Vocabulary { get; set; } = new();

    /// <summary>
    /// Output settings
    /// </summary>
    public OutputSettings Output { get; set; } = new();
}

/// <summary>
/// Audio capture settings
/// </summary>
public class AudioSettings
{
    public string DeviceId { get; set; } = "default";
    public double Gain { get; set; } = 1.0;
    public double SilenceThresholdMs { get; set; } = 1500;
}

/// <summary>
/// Whisper model settings
/// </summary>
public class WhisperSettings
{
    public string ModelPath { get; set; } = string.Empty;
    public string Language { get; set; } = "auto";
}

/// <summary>
/// LLM post-processing settings
/// </summary>
public class LLMSettings
{
    public string Provider { get; set; } = "lm-studio";
    public string Endpoint { get; set; } = "http://localhost:1234/v1";
    public string Model { get; set; } = "auto";
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Activation mode settings
/// </summary>
public class ActivationSettings
{
    public string Mode { get; set; } = "continuous";
    public string Hotkey { get; set; } = "Ctrl+Alt+D";
    public bool KeywordEnabled { get; set; } = true;
    public string Keyword { get; set; } = "Hey Dictate";
}

/// <summary>
/// Custom vocabulary settings
/// </summary>
public class VocabularySettings
{
    public List<string> CustomWords { get; set; } = new();
}

/// <summary>
/// Output settings
/// </summary>
public class OutputSettings
{
    public string Mode { get; set; } = "clipboard";
    public bool ToastEnabled { get; set; } = true;
}

/// <summary>
/// Model information
/// </summary>
public class ModelInfo
{
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Language { get; set; } = "en";
    public string Format { get; set; } = "ggml";
}

/// <summary>
/// Transcription result
/// </summary>
public class TranscriptionResult
{
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public List<string> Alternatives { get; set; } = new();
}