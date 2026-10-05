using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.ViewModels;

public partial class AudioTabViewModel : ObservableObject
{
    private readonly IAudioCaptureService _audioCapture;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private string _selectedDeviceId = "default";

    [ObservableProperty]
    private double _gain = 1.0;

    [ObservableProperty]
    private double _silenceThresholdMs = 1500;

    [ObservableProperty]
    private double _silenceThresholdDb = -40;

    [ObservableProperty]
    private bool _compressorEnabled = true;

    [ObservableProperty]
    private double _compressorThresholdDb = -18;

    [ObservableProperty]
    private double _compressorRatio = 4.0;

    [ObservableProperty]
    private double _currentAudioLevel;

    [ObservableProperty]
    private double _peakAudioLevel;

    [ObservableProperty]
    private AudioDevice? _selectedDevice;

    public ObservableCollection<AudioDevice> AvailableDevices { get; } = new();

    /// <summary>Raised after settings are saved, so the host can push them to the daemon via IPC.</summary>
    public event EventHandler? SettingsSaved;

    public AudioTabViewModel(IAudioCaptureService audioCapture, ISettingsService settingsService)
    {
        _audioCapture = audioCapture;
        _settingsService = settingsService;
        LoadAsync();
    }

    /// <summary>Refresh the device list (e.g. after the daemon connects / devices change).</summary>
    public void ReloadDevices()
    {
        SetAvailableDevices(_audioCapture.GetAvailableDevices());
    }

    public void SetAvailableDevices(IEnumerable<AudioDevice> devices)
    {
        var previousName = SelectedDevice?.FriendlyName;
        AvailableDevices.Clear();
        foreach (var device in devices)
            AvailableDevices.Add(device);
        SelectedDevice = AvailableDevices.FirstOrDefault(d => d.Id == SelectedDeviceId)
            ?? AvailableDevices.FirstOrDefault(d => d.FriendlyName == previousName)
            ?? AvailableDevices.FirstOrDefault();
        if (SelectedDevice is not null)
            SelectedDeviceId = SelectedDevice.Id;
    }

    private async void LoadAsync()
    {
        _loading = true;
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            SelectedDeviceId = settings.Audio.DeviceId;
            Gain = settings.Audio.Gain;
            SilenceThresholdMs = settings.Audio.SilenceThresholdMs;
            SilenceThresholdDb = settings.Audio.SilenceThresholdDb;
            CompressorEnabled = settings.Audio.CompressorEnabled;
            CompressorThresholdDb = settings.Audio.CompressorThresholdDb;
            CompressorRatio = settings.Audio.CompressorRatio;

            ReloadDevices();
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSelectedDeviceChanged(AudioDevice? value) => ScheduleAutoApply();
    partial void OnGainChanged(double value) => ScheduleAutoApply();
    partial void OnSilenceThresholdMsChanged(double value) => ScheduleAutoApply();
    partial void OnSilenceThresholdDbChanged(double value) => ScheduleAutoApply();
    partial void OnCompressorEnabledChanged(bool value) => ScheduleAutoApply();
    partial void OnCompressorThresholdDbChanged(double value) => ScheduleAutoApply();
    partial void OnCompressorRatioChanged(double value) => ScheduleAutoApply();


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
        ApplyTo(settings.Audio);
        await _settingsService.SaveSettingsAsync(settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyTo(AudioSettings audio)
    {
        audio.DeviceId = SelectedDevice?.Id ?? SelectedDeviceId;
        audio.Gain = Gain;
        audio.SilenceThresholdMs = SilenceThresholdMs;
        audio.SilenceThresholdDb = SilenceThresholdDb;
        audio.CompressorEnabled = CompressorEnabled;
        audio.CompressorThresholdDb = CompressorThresholdDb;
        audio.CompressorRatio = CompressorRatio;
    }

    public string CurrentAudioLevelDb => CurrentAudioLevel > 0
        ? $"{20 * Math.Log10(CurrentAudioLevel):0} dB"
        : "-96 dB";

    public string PeakAudioLevelDb => PeakAudioLevel > 0
        ? $"{20 * Math.Log10(PeakAudioLevel):0} dB"
        : "-96 dB";
}
