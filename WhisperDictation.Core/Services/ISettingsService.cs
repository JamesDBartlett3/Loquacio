namespace WhisperDictation.Services;

/// <summary>
/// Service for managing application settings
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Load settings from storage
    /// </summary>
    Task<WhisperDictation.Models.Settings> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Save settings to storage
    /// </summary>
    Task SaveSettingsAsync(WhisperDictation.Models.Settings settings, CancellationToken ct = default);

    /// <summary>
    /// Event fired when settings are changed
    /// </summary>
    event EventHandler? SettingsChanged;
}