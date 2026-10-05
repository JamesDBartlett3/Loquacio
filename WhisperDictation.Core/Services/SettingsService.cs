using Microsoft.Extensions.Logging;
using System.Text.Json;
using WhisperDictation.Models;

namespace WhisperDictation.Services;

/// <summary>
/// Service for managing application settings
/// </summary>
public class SettingsService : ISettingsService, IDisposable
{
    private readonly string _settingsPath;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;
    private Settings _cachedSettings;

    public event EventHandler? SettingsChanged;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;

        // Settings directory: %AppData%\WhisperDictation\
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperDictation");

        Directory.CreateDirectory(settingsDir);

        _settingsPath = Path.Combine(settingsDir, "settings.json");

        // Load settings on initialization
        _cachedSettings = LoadFromFile();

        _logger.LogInformation("Settings loaded from {Path}", _settingsPath);
    }

    public async Task<Settings> GetSettingsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _cachedSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSettingsAsync(Settings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Save to file
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_settingsPath, json, ct);

            // Update cache
            _cachedSettings = settings;

            // Notify listeners
            SettingsChanged?.Invoke(this, EventArgs.Empty);

            _logger.LogDebug("Settings saved to {Path}", _settingsPath);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Settings LoadFromFile()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                if (settings is not null)
                {
                    return settings;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load settings from {Path}, using defaults", _settingsPath);
            }
        }

        // Return default settings if file doesn't exist or is invalid
        return CreateDefaultSettings();
    }

    private static Settings CreateDefaultSettings()
    {
        return new Settings
        {
            Audio = new AudioSettings
            {
                DeviceId = "default",
                Gain = 1.0,
                SilenceThresholdMs = 1500,
                SilenceThresholdDb = -40,
                CompressorEnabled = true,
                CompressorThresholdDb = -18,
                CompressorRatio = 4.0
            },
            Whisper = new WhisperSettings
            {
                ModelPath = string.Empty,
                Language = "auto"
            },
            LLM = new LLMSettings
            {
                Provider = "lm-studio",
                Endpoint = "http://localhost:1234/v1",
                Model = "auto"
            },
            Activation = new ActivationSettings
            {
                Mode = "continuous",
                Hotkey = "Ctrl+Alt+D",
                KeywordEnabled = true,
                Keyword = "Hey Dictate"
            },
            Vocabulary = new VocabularySettings
            {
                CustomWords = new List<string>()
            },
            Output = new OutputSettings
            {
                Mode = "inject",
                ToastEnabled = true
            }
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}