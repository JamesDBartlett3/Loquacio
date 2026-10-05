using System.Text.Json;

namespace Loquacio.Models;

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

    /// <summary>
    /// Tray and window behavior settings
    /// </summary>
    public TraySettings Tray { get; set; } = new();

    /// <summary>
    /// Dictation pipeline FSM settings (stage safety timeouts, per-session runs)
    /// </summary>
    public PipelineSettings Pipeline { get; set; } = new();

    /// <summary>
    /// Deep copy of the settings, frozen at a point in time. The dictation
    /// pipeline snapshots settings once per FSM run so mid-run edits to the
    /// live settings object can never leak into an in-flight run — they apply
    /// at the next run's snapshot instead (settings-freeze-per-run).
    /// </summary>
    public Settings Snapshot() =>
        JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this)) ?? new Settings();
}

/// <summary>
/// Audio capture settings
/// </summary>
public class AudioSettings
{
    public string DeviceId { get; set; } = "default";
    public double Gain { get; set; } = 1.0;
    public double SilenceThresholdMs { get; set; } = 1500;
    public double SilenceThresholdDb { get; set; } = -40;
    public bool CompressorEnabled { get; set; } = true;
    public double CompressorThresholdDb { get; set; } = -18;
    public double CompressorRatio { get; set; } = 4.0;
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
    public string ToggleModeHotkey { get; set; } = "Ctrl+Alt+M";
    public string StopHotkey { get; set; } = "Ctrl+Alt+S";
    public string CopyLastOutputHotkey { get; set; } = "Ctrl+Alt+V";
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
    // "inject" (type into the focused window — default), "type" (alias),
    // or "clipboard" (copy only).
    public string Mode { get; set; } = "inject";
    public bool ToastEnabled { get; set; } = true;
}

/// <summary>
/// Per-stage safety timeouts for the dictation FSM. Each stage that arms a
/// timeout force-cancels its run back to Idle when the timeout expires, so a
/// hung Whisper call or LLM request can never wedge the pipeline.
/// </summary>
public class PipelineSettings
{
    /// <summary>Master switch for stage timeouts (0 or disabled → no timeout).</summary>
    public bool EnableStageTimeouts { get; set; } = true;

    /// <summary>Hard cap for a single Whisper transcription, in seconds.</summary>
    public int TranscribeTimeoutSeconds { get; set; } = 120;

    /// <summary>Hard cap for LLM post-processing of one segment, in seconds.</summary>
    public int PostProcessTimeoutSeconds { get; set; } = 60;

    /// <summary>Hard cap for text output (clipboard copy / injection), in seconds.</summary>
    public int OutputTimeoutSeconds { get; set; } = 30;
}

/// <summary>/// System tray and window behavior settings
/// </summary>
public class TraySettings
{
    /// <summary>
    /// Minimize to tray instead of taskbar when the window is minimized.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Close to tray instead of exiting when the window is closed.
    /// </summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Start the application minimized to tray.
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Launch automatically when Windows starts.
    /// </summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// Show balloon notifications for transcription results.
    /// </summary>
    public bool NotificationsEnabled { get; set; } = true;
}