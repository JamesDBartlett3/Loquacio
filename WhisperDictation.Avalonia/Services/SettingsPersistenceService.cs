using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WhisperDictation.Avalonia.Services;

/// <summary>
/// Persists all controller settings to a JSON file.
/// </summary>
public sealed class SettingsPersistenceService
{
    private readonly string _filePath;
    private readonly ILogger<SettingsPersistenceService> _logger;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsPersistenceService(ILogger<SettingsPersistenceService> logger, string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "whisper-dictation", "avalonia-settings.json");
        _logger = logger;
    }

    public AvaloniaSettings? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No settings file found at {Path}, using defaults", _filePath);
                return null;
            }
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AvaloniaSettings>(json, _jsonOpts);
            _logger.LogInformation("Loaded settings from {Path}", _filePath);
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings from {Path}", _filePath);
            return null;
        }
    }

    public void Save(AvaloniaSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, _jsonOpts);
            File.WriteAllText(_filePath, json);
            _logger.LogInformation("Saved settings to {Path}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _filePath);
        }
    }
}

public sealed class AvaloniaSettings
{
    // General
    public string ActivationMode { get; set; } = "continuous";
    public string HotkeyDisplay { get; set; } = "Ctrl+Alt+D";
    public bool KeywordEnabled { get; set; } = true;
    public string Keyword { get; set; } = "Hey Dictate";
    public string OutputMode { get; set; } = "clipboard";
    public bool NotificationsEnabled { get; set; } = true;

    // Audio
    public string SelectedDeviceId { get; set; } = "default";
    public double Gain { get; set; } = 1.0;
    public double SilenceThresholdMs { get; set; } = 1500;

    // Whisper
    public string ModelSize { get; set; } = "base";
    public string Language { get; set; } = "auto";

    // LLM
    public bool LlmEnabled { get; set; } = true;
    public string LlmProvider { get; set; } = "lm-studio";
    public string LlmEndpoint { get; set; } = "http://localhost:1234/v1";
    public string LlmModel { get; set; } = "auto";

    // Vocabulary
    public List<string> CustomWords { get; set; } = [];

    // Window
    public double WindowWidth { get; set; } = 800;
    public double WindowHeight { get; set; } = 600;
    public bool MinimizeToTray { get; set; } = true;
}
