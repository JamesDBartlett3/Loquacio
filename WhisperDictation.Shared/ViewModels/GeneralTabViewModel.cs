using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperDictation.Models;
using WhisperDictation.Services;

namespace WhisperDictation.ViewModels;

public partial class GeneralTabViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IAutoStartService _autoStartService;
    private CancellationTokenSource? _autoApplyCts;
    private bool _loading;

    [ObservableProperty]
    private string _activationMode = "continuous";

    [ObservableProperty]
    private string _hotkey = "Ctrl+Alt+D";

    [ObservableProperty]
    private string _toggleModeHotkey = "Ctrl+Alt+M";

    [ObservableProperty]
    private string _stopHotkey = "Ctrl+Alt+S";

    [ObservableProperty]
    private string _copyLastOutputHotkey = "Ctrl+Alt+V";

    [ObservableProperty]
    private string _outputMode = "clipboard";

    [ObservableProperty]
    private bool _toastEnabled = true;

    [ObservableProperty]
    private bool _isCapturingHotkey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCapturingToggle))]
    [NotifyPropertyChangedFor(nameof(IsCapturingToggleMode))]
    [NotifyPropertyChangedFor(nameof(IsCapturingStop))]
    [NotifyPropertyChangedFor(nameof(IsCapturingCopy))]
    private string _capturingAction = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Legacy daemon-side settings kept for persistence/IPC round-trips
    // (keyword activation is disabled; Avalonia persists these too).
    [ObservableProperty]
    private bool _keywordEnabled = true;

    [ObservableProperty]
    private string _keyword = "Hey Dictate";

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    public string[] ActivationModes { get; } = { "continuous", "push-to-talk" };
    public string[] OutputModes { get; } = { "inject", "clipboard", "type" };

    /// <summary>Per-action capture indicators for the hotkey row buttons.</summary>
    public bool IsCapturingToggle => IsCapturingHotkey && CapturingAction == "toggle";
    public bool IsCapturingToggleMode => IsCapturingHotkey && CapturingAction == "mode";
    public bool IsCapturingStop => IsCapturingHotkey && CapturingAction == "stop";
    public bool IsCapturingCopy => IsCapturingHotkey && CapturingAction == "copy";

    /// <summary>Raised after settings are saved, so the host can push them to the daemon via IPC.</summary>
    public event EventHandler? SettingsSaved;

    public GeneralTabViewModel(ISettingsService settingsService, IHotkeyService hotkeyService, IAutoStartService autoStartService)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _autoStartService = autoStartService;
        LoadAsync();
    }

    private async void LoadAsync()
    {
        try
        {
            _loading = true;
            var settings = await _settingsService.GetSettingsAsync();
            ActivationMode = settings.Activation.Mode;
            Hotkey = settings.Activation.Hotkey;
            ToggleModeHotkey = settings.Activation.ToggleModeHotkey;
            StopHotkey = settings.Activation.StopHotkey;
            CopyLastOutputHotkey = settings.Activation.CopyLastOutputHotkey;
            OutputMode = settings.Output.Mode;
            ToastEnabled = settings.Output.ToastEnabled;
            AutoStart = _autoStartService.IsEnabled;
        }
        catch (Exception)
        {
            StatusMessage = "Failed to load settings — using defaults";
        }
        finally
        {
            _loading = false;
        }
    }

    // ── Auto-apply: any setting change is saved (debounced) and pushed to the daemon ──

    partial void OnActivationModeChanged(string value) => ScheduleAutoApply();
    partial void OnOutputModeChanged(string value) => ScheduleAutoApply();
    partial void OnToastEnabledChanged(bool value) => ScheduleAutoApply();
    partial void OnHotkeyChanged(string value) => ScheduleAutoApply();
    partial void OnToggleModeHotkeyChanged(string value) => ScheduleAutoApply();
    partial void OnStopHotkeyChanged(string value) => ScheduleAutoApply();
    partial void OnCopyLastOutputHotkeyChanged(string value) => ScheduleAutoApply();

    private void ScheduleAutoApply()
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

    // ── Hotkey capture ──

    [RelayCommand]
    private void StartCapture(string? action)
    {
        if (IsCapturingHotkey && CapturingAction == action)
        {
            IsCapturingHotkey = false;
            CapturingAction = string.Empty;
            StatusMessage = "Hotkey capture cancelled";
            return;
        }

        CapturingAction = action ?? "toggle";
        IsCapturingHotkey = true;
        StatusMessage = "Press a key combination...";
    }

    /// <summary>
    /// Clears one hotkey binding (empty string = unbound). The daemon skips
    /// empty bindings when it re-registers hotkeys, so the OS binding is dropped.
    /// </summary>
    [RelayCommand]
    private void ClearHotkey(string? action)
    {
        if (IsCapturingHotkey)
        {
            IsCapturingHotkey = false;
            CapturingAction = string.Empty;
        }

        switch (action)
        {
            case "mode": ToggleModeHotkey = string.Empty; break;
            case "stop": StopHotkey = string.Empty; break;
            case "copy": CopyLastOutputHotkey = string.Empty; break;
            default: Hotkey = string.Empty; break;
        }

        StatusMessage = "Hotkey cleared";
    }

    /// <summary>
    /// Called from the View when a key is pressed during capture mode.
    /// Uses platform-agnostic HotkeyKey and HotkeyModifiers; routes the new
    /// combo to the action currently being captured.
    /// </summary>
    public void CaptureKey(HotkeyKey key, HotkeyModifiers modifiers)
    {
        if (!IsCapturingHotkey) return;

        var action = CapturingAction;
        IsCapturingHotkey = false;
        CapturingAction = string.Empty;

        if (key == HotkeyKey.Escape)
        {
            StatusMessage = "Hotkey capture cancelled";
            return;
        }

        if (key == HotkeyKey.None || modifiers == HotkeyModifiers.None)
        {
            StatusMessage = "Hotkey must include a modifier (e.g. Ctrl+Alt+D)";
            return;
        }

        var newHotkey = IHotkeyService.FormatHotkey(key, modifiers);

        // Reject combos already bound to a different action — the daemon would
        // have to pick one, which is never what the user wants.
        var bindings = new (string Action, string Value, string Name)[]
        {
            ("toggle", Hotkey, "Toggle Listening"),
            ("mode", ToggleModeHotkey, "Toggle Mode"),
            ("stop", StopHotkey, "Stop Listening"),
            ("copy", CopyLastOutputHotkey, "Copy Last Output"),
        };
        foreach (var (act, existing, name) in bindings)
        {
            if (act == action) continue; // the action being re-bound
            if (string.Equals(existing, newHotkey, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Conflict: {newHotkey} is already used by '{name}'";
                return;
            }
        }

        var target = action switch
        {
            "mode" => ToggleModeHotkey,
            "stop" => StopHotkey,
            "copy" => CopyLastOutputHotkey,
            _ => Hotkey,
        };
        var unchanged = string.Equals(target, newHotkey, StringComparison.OrdinalIgnoreCase);

        switch (action)
        {
            case "mode": ToggleModeHotkey = newHotkey; break;
            case "stop": StopHotkey = newHotkey; break;
            case "copy": CopyLastOutputHotkey = newHotkey; break;
            default: Hotkey = newHotkey; break;
        }

        // Local registration is a no-op on the controller (the daemon owns real
        // hotkeys and re-registers when the activation settings are pushed).
        if (!unchanged)
        {
            _hotkeyService.Register(new HotkeyBinding(key, modifiers, action switch
            {
                "mode" => HotkeyActions.ToggleMode,
                "stop" => HotkeyActions.Stop,
                "copy" => HotkeyActions.CopyLastOutput,
                _ => HotkeyActions.ToggleListening,
            }));
        }

        StatusMessage = $"Hotkey set to {newHotkey}";
    }

    public async Task SaveSettings()
    {
        var settings = await _settingsService.GetSettingsAsync();
        settings.Activation.Mode = ActivationMode;
        settings.Activation.Hotkey = Hotkey;
        settings.Activation.ToggleModeHotkey = ToggleModeHotkey;
        settings.Activation.StopHotkey = StopHotkey;
        settings.Activation.CopyLastOutputHotkey = CopyLastOutputHotkey;
        settings.Activation.KeywordEnabled = KeywordEnabled;
        settings.Activation.Keyword = Keyword;
        settings.Output.Mode = OutputMode;
        settings.Output.ToastEnabled = ToastEnabled;
        settings.Tray.MinimizeToTray = MinimizeToTray;
        settings.Tray.CloseToTray = CloseToTray;
        settings.Tray.StartMinimized = StartMinimized;
        settings.Tray.NotificationsEnabled = NotificationsEnabled;

        if (AutoStart && !_autoStartService.IsEnabled)
            _autoStartService.Enable();
        else if (!AutoStart && _autoStartService.IsEnabled)
            _autoStartService.Disable();

        await _settingsService.SaveSettingsAsync(settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyTo(ActivationSettings activation)
    {
        activation.Mode = ActivationMode;
        activation.Hotkey = Hotkey;
        activation.ToggleModeHotkey = ToggleModeHotkey;
        activation.StopHotkey = StopHotkey;
        activation.CopyLastOutputHotkey = CopyLastOutputHotkey;
        activation.KeywordEnabled = KeywordEnabled;
        activation.Keyword = Keyword;
    }

    public void ApplyTo(OutputSettings output)
    {
        output.Mode = OutputMode;
        output.ToastEnabled = ToastEnabled;
    }

    public void ApplyTo(TraySettings tray)
    {
        tray.MinimizeToTray = MinimizeToTray;
        tray.CloseToTray = CloseToTray;
        tray.StartMinimized = StartMinimized;
        tray.NotificationsEnabled = NotificationsEnabled;
    }
}
