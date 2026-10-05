using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.ViewModels;

public partial class WhisperTabViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IModelManagerService _modelManager;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    private string _modelPath = string.Empty;

    [ObservableProperty]
    private string _language = "auto";

    [ObservableProperty]
    private string _selectedModelSize = "base";

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    [ObservableProperty]
    private double _downloadProgress;

    public string[] ModelSizes { get; } = { "tiny", "base", "small", "medium", "large-v3" };
    public string[] Languages { get; } =
    {
        "auto", "en", "es", "fr", "de", "it", "pt", "nl", "ja", "ko", "zh", "ru",
        "uk", "pl", "tr", "sv", "da", "no", "fi", "cs", "el", "he", "hi", "th", "vi", "ar",
    };

    /// <summary>True when the selected model is downloaded and NOT the active model — safe to delete.</summary>
    [ObservableProperty]
    private bool _canDeleteSelectedModel;

    /// <summary>Raised after settings are saved, so the host can push them to the daemon via IPC.</summary>
    public event EventHandler? SettingsSaved;

    /// <summary>True when no Whisper model is configured — drives the dashboard warning banner.</summary>
    public bool HasModelConfigured => !string.IsNullOrWhiteSpace(ModelPath);

    public WhisperTabViewModel(ISettingsService settingsService, IModelManagerService modelManager)
    {
        _settingsService = settingsService;
        _modelManager = modelManager;
        LoadAsync();
    }

    private async void LoadAsync()
    {
        _loading = true;
        var storedLanguage = string.Empty;
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            ModelPath = settings.Whisper.ModelPath;
            Language = settings.Whisper.Language;
            storedLanguage = settings.Whisper.Language;

            // Reflect the configured model (if any) in the size dropdown
            var name = _modelManager.AvailableModels.FirstOrDefault(m =>
                !string.IsNullOrEmpty(ModelPath) &&
                string.Equals(Path.GetFileName(ModelPath), m.GgmlFileName, StringComparison.OrdinalIgnoreCase));
            if (name is not null) SelectedModelSize = name.Name;
        }
        finally
        {
            _loading = false;
        }

        // Default the language to the OS UI language on first use ("auto" is
        // treated as unset). The user can override it and the override persists.
        if (string.IsNullOrWhiteSpace(storedLanguage) || storedLanguage == "auto")
        {
            var osLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (Languages.Contains(osLanguage))
            {
                Language = osLanguage;
                ScheduleAutoApply(); // persist the detected default
            }
        }

        UpdateDownloadStatus();
    }




    // ── Auto-apply: any setting change is saved (debounced) and pushed ──

    private CancellationTokenSource? _autoApplyCts;
    private bool _loading;

    /// <summary>Debounced save-and-push; call from On*Changed partials.</summary>
    protected void ScheduleAutoApply()
    {
        if (_loading) return;

        _autoApplyCts?.Cancel();
        _autoApplyCts = new CancellationTokenSource();
        var ct = _autoApplyCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, ct);
                if (ct.IsCancellationRequested) return;
                await SaveSettings();
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        var settings = await _settingsService.GetSettingsAsync();
        ApplyTo(settings.Whisper);
        await _settingsService.SaveSettingsAsync(settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDownloadStatus()
    {
        if (string.IsNullOrEmpty(SelectedModelSize)) return;

        var model = _modelManager.AvailableModels.FirstOrDefault(m => m.Name == SelectedModelSize);
        if (model == null) return;

        var modelPath = _modelManager.GetModelPath(model);
        var downloaded = _modelManager.IsModelDownloaded(model);

        if (downloaded)
        {
            DownloadStatus = $"✓ {model.DisplayName} is ready";
            ModelPath = _modelManager.GetModelPath(model);
        }
        else
        {
            DownloadStatus = $"{model.DisplayName} — not downloaded";
        }

        // Only unused (downloaded but inactive) models may be deleted
        CanDeleteSelectedModel = downloaded &&
            !string.Equals(modelPath, ModelPath, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSelectedModelSizeChanged(string value)
    {
        UpdateDownloadStatus();
        ScheduleAutoApply();
    }

    partial void OnLanguageChanged(string value) => ScheduleAutoApply();

    [RelayCommand]
    private async Task DeleteModel()
    {
        if (!CanDeleteSelectedModel || string.IsNullOrEmpty(SelectedModelSize)) return;

        var model = _modelManager.AvailableModels.FirstOrDefault(m => m.Name == SelectedModelSize);
        if (model == null) return;

        try
        {
            await _modelManager.DeleteModelAsync(model);
            DownloadStatus = $"{model.DisplayName} deleted";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Error deleting {model.DisplayName}: {ex.Message}";
        }
        finally
        {
            UpdateDownloadStatus();
        }
    }

    [RelayCommand]
    private async Task DownloadModel()
    {
        if (string.IsNullOrEmpty(SelectedModelSize)) return;

        var model = _modelManager.AvailableModels.FirstOrDefault(m => m.Name == SelectedModelSize);
        if (model == null)
        {
            DownloadStatus = $"Unknown model: {SelectedModelSize}";
            return;
        }

        if (_modelManager.IsModelDownloaded(model))
        {
            DownloadStatus = $"✓ {model.DisplayName} is already downloaded";
            ModelPath = _modelManager.GetModelPath(model);
            return;
        }

        _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatus = $"Downloading {model.DisplayName}...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p * 100;
                DownloadStatus = $"Downloading {model.DisplayName}: {p * 100:F0}%";
            });

            await _modelManager.DownloadModelAsync(model, progress, _downloadCts.Token);

            ModelPath = _modelManager.GetModelPath(model);

            // Save to settings
            var settings = await _settingsService.GetSettingsAsync();
            settings.Whisper.ModelPath = ModelPath;
            await _settingsService.SaveSettingsAsync(settings);

            DownloadStatus = $"✓ {model.DisplayName} downloaded and ready";
            SettingsSaved?.Invoke(this, EventArgs.Empty); // model path changed — notify daemon
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Download cancelled";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    public void ApplyTo(WhisperSettings whisper)
    {
        whisper.ModelPath = ModelPath;
        whisper.Language = Language;
    }
}
