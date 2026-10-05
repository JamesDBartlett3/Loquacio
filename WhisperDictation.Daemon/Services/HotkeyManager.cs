using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using WhisperDictation.Daemon.Models;
using WhisperDictation.Daemon.Interop;

namespace WhisperDictation.Daemon.Services;

/// <summary>
/// Global hotkey manager for the daemon. Registers system-wide hotkeys
/// that toggle listening from any application. Hotkeys are configurable
/// via appsettings.json.
///
/// On Linux: Uses X11 XGrabKey for global hotkey capture.
/// On Windows: Uses RegisterHotKey via a hidden message-only window.
/// </summary>
public sealed class HotkeyManager(
    IActivationManagerService activationManager,
    IConfiguration configuration,
    ILogger<HotkeyManager> logger,
    IX11Interop? x11Interop = null,
    ISettingsService? settingsService = null,
    IBackgroundTranscriptionService? transcription = null,
    IClipboardService? clipboard = null
) : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _eventLoopTask;
    private IntPtr _x11Display;
    private readonly List<(int keycode, uint modifiers, string action)> _grabbedX11Keys = [];
    private readonly List<(int id, string action)> _registeredWin32Actions = [];
    private IntPtr _win32Hwnd;
    private uint _win32ThreadId;
    private bool _disposed;
    private static string? _lastX11Error;
    private List<HotkeyEntry> _hotkeyEntries = [];

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Load hotkey configuration
        await LoadConfiguration();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            InitializeX11();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            InitializeWin32();
        }
        else
        {
            logger.LogWarning("Hotkey manager not supported on {OS}", RuntimeInformation.OSDescription);
        }

        
    }

    // ── Configuration ──

    private async Task LoadConfiguration()
    {
        var options = configuration.GetSection("Hotkeys").Get<DaemonHotkeyOptions>();

        if (options?.Hotkeys is { Count: > 0 } entries)
        {
            _hotkeyEntries = entries;
            logger.LogInformation("Loaded {Count} hotkey binding(s) from configuration", entries.Count);
            foreach (var entry in entries)
            {
                logger.LogDebug("  Hotkey: {Mods}+{Key} → {Action}", entry.Modifiers, entry.Key, entry.Action);
            }
        }
        else
        {
            // Default hotkeys
            _hotkeyEntries =
            [
                new HotkeyEntry { Key = "F1", Modifiers = "Ctrl+Alt", Action = "toggle" },
                new HotkeyEntry { Key = "Space", Modifiers = "Ctrl+Shift", Action = "toggle" },
            ];
            logger.LogInformation("No hotkeys in configuration, using defaults: Ctrl+Alt+F1, Ctrl+Shift+Space");
        }

        // Merge the user-configured hotkeys (set via the controller UI) as
        // bindings, unless appsettings already covers them. Each action has
        // its own configurable combo.
        if (settingsService is not null)
        {
            try
            {
                var settings = await settingsService.GetSettingsAsync();
                AddBinding(settings.Activation.Hotkey, "toggle");
                AddBinding(settings.Activation.ToggleModeHotkey, "mode");
                AddBinding(settings.Activation.StopHotkey, "stop");
                AddBinding(settings.Activation.CopyLastOutputHotkey, "copy");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not merge settings hotkeys into daemon hotkeys");
            }
        }
    }

    /// <summary>
    /// Adds a "Mods+Key" binding for the given action unless the same combo is
    /// already bound (first binding wins; duplicates would conflict with
    /// themselves at the OS level). Bindings that parse to nothing are skipped.
    /// </summary>
    private void AddBinding(string? hotkeyString, string action)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString)) return;

        var (key, modifiers) = IHotkeyService.ParseHotkeyString(hotkeyString);
        if (key == HotkeyKey.None || modifiers == HotkeyModifiers.None) return;

        var keyName = key.ToString();
        var modsName = IHotkeyService.FormatHotkey(key, modifiers)
            .Split('+')[..^1]
            .Aggregate((a, b) => a + "+" + b);

        var duplicate = _hotkeyEntries.FirstOrDefault(e =>
            string.Equals(e.Key, keyName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Modifiers, modsName, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            logger.LogWarning(
                "Hotkey conflict: {Hotkey} for action '{Action}' is already bound to '{ExistingAction}' — keeping the existing binding",
                hotkeyString, action, duplicate.Action);
            return;
        }

        _hotkeyEntries.Add(new HotkeyEntry { Key = keyName, Modifiers = modsName, Action = action });
    }

    /// <summary>
    /// Parses a modifier string like "Ctrl+Alt" into the appropriate platform mask.
    /// </summary>
    private static uint ParseModifiersLinux(string mods)
    {
        uint result = 0;
        foreach (var part in mods.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result += part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => LibX11.ControlMask,
                "alt" => LibX11.Mod1Mask,
                "shift" => LibX11.ShiftMask,
                "win" or "super" or "meta" => LibX11.Mod4Mask,
                _ => 0,
            };
        }
        return result;
    }

    /// <summary>
    /// Parses a modifier string into Win32 MOD_* flags.
    /// </summary>
    private static uint ParseModifiersWin32(string mods)
    {
        uint result = 0;
        foreach (var part in mods.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Win32.MOD_CONTROL,
                "alt" => Win32.MOD_ALT,
                "shift" => Win32.MOD_SHIFT,
                "win" or "super" => Win32.MOD_WIN,
                _ => 0u,
            };
        }
        return result | Win32.MOD_NOREPEAT; // Prevent repeat-fire while held
    }

    // ── X11 Implementation ──

    private void InitializeX11()
    {
        if (x11Interop is null)
        {
            logger.LogWarning("X11 interop not available. Global hotkeys disabled.");
            return;
        }

        try
        {
            _x11Display = x11Interop.OpenDisplay();
            if (_x11Display == IntPtr.Zero)
            {
                logger.LogWarning("X11: Failed to open display. Global hotkeys not available. " +
                    "Ensure DISPLAY is set and X server is running.");
                return;
            }

            // Set up error handler
            x11Interop.SetErrorHandler((IntPtr d, ref XErrorEvent ev) =>
            {
                _lastX11Error = $"X11 Error: code={ev.error_code}, request={ev.request_code}";
                return 0;
            });

            // Register all configured hotkeys
            foreach (var entry in _hotkeyEntries)
            {
                RegisterX11Hotkey(entry);
            }

            if (_grabbedX11Keys.Count == 0)
            {
                logger.LogWarning("X11: No hotkeys registered successfully.");
                return;
            }

            // Start background event loop
            _eventLoopTask = Task.Run(() => X11EventLoop(_cts!.Token), _cts!.Token);

            logger.LogInformation("X11: Registered {Count} global hotkey(s). Event loop started.", _grabbedX11Keys.Count);
        }
        catch (DllNotFoundException)
        {
            logger.LogWarning("libX11 not found. Global hotkeys not available. " +
                "Install libx11-6 (apt: libx11-6, dnf: libX11) to enable X11 hotkeys.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "X11 hotkey initialization failed.");
        }
    }

    private void RegisterX11Hotkey(HotkeyEntry entry)
    {
        var keysym = x11Interop!.StringToKeysym(entry.Key);
        if (keysym == 0)
        {
            // Try common keysym prefixes
            keysym = x11Interop.StringToKeysym("XK_" + entry.Key);
        }
        if (keysym == 0)
        {
            logger.LogWarning("X11: Unknown key name '{KeyName}'", entry.Key);
            return;
        }

        var keycode = x11Interop.KeysymToKeycode(_x11Display, keysym);
        if (keycode == 0)
        {
            logger.LogWarning("X11: No keycode for key '{KeyName}'", entry.Key);
            return;
        }

        var modifiers = ParseModifiersLinux(entry.Modifiers);
        GrabX11Key(keycode, modifiers, entry.Action);
    }

    private void GrabX11Key(int keycode, uint modifiers, string action)
    {
        var root = x11Interop!.DefaultRootWindow(_x11Display);
        _lastX11Error = null;

        // Grab with NumLock and CapsLock variants too
        var modifierVariants = GetModifierVariants(modifiers);
        int successCount = 0;

        foreach (var modVariant in modifierVariants)
        {
            x11Interop.Sync(_x11Display, false);
            var result = x11Interop.GrabKey(
                _x11Display, keycode, modVariant,
                root, true,
                LibX11.GrabModeAsync, LibX11.GrabModeAsync);
            x11Interop.Sync(_x11Display, false);

            // XGrabKey returns GrabSuccess (0) on success; non-zero indicates failure
            if (_lastX11Error == null && result == 0)
            {
                successCount++;
            }
        }

        if (successCount > 0)
        {
            _grabbedX11Keys.Add((keycode, modifiers, action));
            logger.LogDebug("X11: Grabbed key {Keycode} with modifiers {Modifiers:X} ({Count} variants) → {Action}",
                keycode, modifiers, successCount, action);
        }
        else
        {
            logger.LogWarning("X11: Failed to grab key {Keycode} (mods {Modifiers:X}). " +
                "Another application may have already grabbed it.", keycode, modifiers);
        }
    }

    private static IEnumerable<uint> GetModifierVariants(uint baseModifiers)
    {
        yield return baseModifiers;
        yield return baseModifiers | LibX11.Mod2Mask;
        yield return baseModifiers | LibX11.LockMask;
        yield return baseModifiers | LibX11.Mod2Mask | LibX11.LockMask;
    }

    private async Task X11EventLoop(CancellationToken ct)
    {
        logger.LogInformation("X11 hotkey event loop started.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                int pending = x11Interop!.Pending(_x11Display);
                if (pending > 0)
                {
                    var ev = new XEvent();
                    x11Interop.NextEvent(_x11Display, ref ev);

                    if (ev.type == LibX11.KeyPress)
                    {
                        logger.LogDebug("X11: Hotkey pressed (keycode={Keycode}, mods={Mods:X})",
                            ev.keycode, ev.state);

                        // Match the pressed combo to a grabbed binding; fall back
                        // to toggle when the state mask doesn't match exactly.
                        var action = _grabbedX11Keys.FirstOrDefault(k => k.keycode == ev.keycode && k.modifiers == ev.state).action
                                     ?? _grabbedX11Keys.FirstOrDefault(k => k.keycode == ev.keycode).action
                                     ?? "toggle";
                        await DispatchHotkeyActionAsync(action);
                    }
                }
                else
                {
                    await Task.Delay(50, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in X11 event loop");
                await Task.Delay(1000, ct);
            }
        }

        logger.LogInformation("X11 hotkey event loop stopped.");
    }

    private void UngrabAllX11Keys()
    {
        if (_x11Display == IntPtr.Zero || x11Interop is null) return;

        var root = x11Interop.DefaultRootWindow(_x11Display);
        foreach (var (keycode, modifiers, _) in _grabbedX11Keys)
        {
            foreach (var modVariant in GetModifierVariants(modifiers))
            {
                try { x11Interop.UngrabKey(_x11Display, keycode, modVariant, root); }
                catch { }
            }
        }
        _grabbedX11Keys.Clear();
        x11Interop.Flush(_x11Display);
    }

    // ── Win32 Implementation ──

    private void InitializeWin32()
    {
        // The window and its message pump must share a thread. WM_HOTKEY is
        // delivered to the thread that owns the window, not to an arbitrary
        // worker that later calls GetMessage for that window.
        _eventLoopTask = Task.Run(() => Win32MessageLoop(_cts!.Token), _cts!.Token);
    }

    private async Task Win32MessageLoop(CancellationToken ct)
    {
        logger.LogInformation("Win32 hotkey message loop started.");
        _win32ThreadId = Win32.GetCurrentThreadId();
        await InitializeWin32OnPumpThread();
        if (_win32Hwnd == IntPtr.Zero)
            return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // GetMessage blocks until a message arrives; use a timeout check
                var result = Win32.GetMessage(out MSG msg, _win32Hwnd, 0, 0);
                if (result == 0) // WM_QUIT
                    break;
                if (result == -1) // Error
                {
                    logger.LogError("Win32: GetMessage returned error");
                    await Task.Delay(1000, ct);
                    continue;
                }

                if (msg.message == Win32.WM_HOTKEY)
                {
                    logger.LogDebug("Win32: WM_HOTKEY received (id={Id})", msg.wParam);
                    var action = _registeredWin32Actions.FirstOrDefault(a => a.id == (int)msg.wParam).action ?? "toggle";
                    await DispatchHotkeyActionAsync(action);
                }

                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessage(ref msg);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Win32 message loop");
                await Task.Delay(1000, ct);
            }
        }

        logger.LogInformation("Win32 hotkey message loop stopped.");
    }

    private async Task InitializeWin32OnPumpThread()
    {
        try
        {
            // Create a message-only window to receive WM_HOTKEY
            var hInstance = Win32.GetModuleHandle(null);
            _win32Hwnd = Win32.CreateWindowEx(
                Win32.WS_EX_MESSAGEBOX, // Message-only window
                "Static", "", 0,
                0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            if (_win32Hwnd == IntPtr.Zero)
            {
                logger.LogError("Win32: Failed to create message-only window for hotkeys.");
                return;
            }

            // Register each hotkey
            int hotkeyId = 0;
            foreach (var entry in _hotkeyEntries)
            {
                var vk = KeyNameToVirtualKey(entry.Key);
                if (vk == 0)
                {
                    logger.LogWarning("Win32: Unknown key name '{KeyName}'", entry.Key);
                    continue;
                }

                var mods = ParseModifiersWin32(entry.Modifiers);
                if (Win32.RegisterHotKey(_win32Hwnd, hotkeyId, mods, vk))
                {
                    _registeredWin32Actions.Add((hotkeyId, entry.Action));
                    logger.LogDebug("Win32: Registered hotkey {Mods}+{Key} (id={Id}) → {Action}",
                        entry.Modifiers, entry.Key, hotkeyId, entry.Action);
                }
                else
                {
                    // Registration fails when another application (or the OS)
                    // already owns the combo — surface it as a conflict.
                    logger.LogWarning("Win32: Hotkey conflict — {Mods}+{Key} for action '{Action}' is already in use by another application",
                        entry.Modifiers, entry.Key, entry.Action);
                }
                hotkeyId++;
            }

            if (_registeredWin32Actions.Count == 0)
            {
                logger.LogWarning("Win32: No hotkeys registered successfully.");
                Win32.DestroyWindow(_win32Hwnd);
                _win32Hwnd = IntPtr.Zero;
                return;
            }

            logger.LogInformation("Win32: Registered {Count} hotkey(s)", _registeredWin32Actions.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Win32 hotkey initialization failed.");
        }
    }

    /// <summary>
    /// Converts a key name string to a Win32 virtual-key code.
    /// </summary>
    private static uint KeyNameToVirtualKey(string keyName)
    {
        // Function keys
        if (keyName.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(keyName[1..], out int fn))
        {
            return fn switch
            {
                >= 1 and <= 12 => Win32.VK_F1 + (uint)(fn - 1),
                _ => 0,
            };
        }

        return keyName.ToUpperInvariant() switch
        {
            "SPACE" => Win32.VK_SPACE,
            "ENTER" or "RETURN" => Win32.VK_RETURN,
            "ESCAPE" or "ESC" => Win32.VK_ESCAPE,
            "BACK" or "BACKSPACE" => Win32.VK_BACK,
            "TAB" => Win32.VK_TAB,
            // Single letters A-Z
            _ when keyName.Length == 1 && char.IsLetter(keyName[0]) => (uint)(char.ToUpper(keyName[0])),
            // Single digits 0-9
            _ when keyName.Length == 1 && char.IsDigit(keyName[0]) => (uint)(keyName[0]),
            _ => 0,
        };
    }

    /// <summary>
    /// Routes a hotkey activation to its configured action. Actions mirror
    /// HotkeyEntry.Action: "toggle", "mode", "stop", "copy".
    /// </summary>
    private async Task DispatchHotkeyActionAsync(string action)
    {
        switch (action)
        {
            case "mode":
                await activationManager.CycleModeAsync();
                break;
            case "stop":
                await activationManager.StopListeningAsync();
                break;
            case "copy":
                var text = transcription?.LastResult?.Text;
                if (string.IsNullOrEmpty(text))
                {
                    logger.LogDebug("Copy-last-output hotkey pressed but no transcription available");
                    break;
                }
                await clipboard!.CopyToClipboardAsync(text);
                break;
            default: // "toggle"
                await activationManager.ToggleListeningAsync();
                break;
        }
    }

    private void UnregisterAllWin32Hotkeys()
    {
        if (_win32Hwnd == IntPtr.Zero) return;

        foreach (var (id, _) in _registeredWin32Actions)
        {
            try { Win32.UnregisterHotKey(_win32Hwnd, id); }
            catch { }
        }
        _registeredWin32Actions.Clear();

        try { Win32.DestroyWindow(_win32Hwnd); }
        catch { }
        _win32Hwnd = IntPtr.Zero;
    }

    // ── Public API ──

    public async Task TriggerToggleAsync()
    {
        await activationManager.ToggleListeningAsync();
    }

    /// <summary>
    /// Re-load hotkey configuration (including Settings.Activation.Hotkey) and
    /// re-register all platform hotkeys. Called after settings updates via IPC
    /// so hotkey changes take effect without a daemon restart.
    /// </summary>
    public async Task ReapplyHotkeysAsync(CancellationToken ct = default)
    {
        if (_disposed) return;

        logger.LogInformation("Reapplying hotkey configuration");

        // Tear down current registrations
        _cts?.Cancel();
        if (_eventLoopTask is not null)
        {
            try { await Task.WhenAny(_eventLoopTask, Task.Delay(TimeSpan.FromSeconds(2), ct)); } catch { }
        }
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            UngrabAllX11Keys();
            if (_x11Display != IntPtr.Zero && x11Interop is not null)
            {
                x11Interop.CloseDisplay(_x11Display);
                _x11Display = IntPtr.Zero;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            UnregisterAllWin32Hotkeys();
        }

        await LoadConfiguration();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            InitializeX11();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            InitializeWin32();
    }

    // ── Cleanup ──

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            UngrabAllX11Keys();
            if (_x11Display != IntPtr.Zero && x11Interop is not null)
            {
                x11Interop.CloseDisplay(_x11Display);
                _x11Display = IntPtr.Zero;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (_win32ThreadId != 0)
                Win32.PostThreadMessage(_win32ThreadId, (uint)Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            UnregisterAllWin32Hotkeys();
        }

        try { _eventLoopTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch { }

        _cts?.Dispose();
    }
}
