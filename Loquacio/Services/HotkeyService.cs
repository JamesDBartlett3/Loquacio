using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Loquacio.Models;

namespace Loquacio.Services;

/// <summary>
/// Win32 implementation of global hotkey registration via RegisterHotKey.
/// Uses a hidden HwndSource to receive WM_HOTKEY messages.
/// </summary>
public class HotkeyService : IHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_DESTROY = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly ILogger<HotkeyService> _logger;
    private readonly Dictionary<int, HotkeyBinding> _registered = new();
    private readonly Dictionary<string, int> _actionToId = new(StringComparer.OrdinalIgnoreCase);
    private HwndSource? _source;
    private int _nextId = 0xC000;

    public event EventHandler<string>? HotkeyPressed;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
        InitializeHwndSource();
    }

    private void InitializeHwndSource()
    {
        var parameters = new HwndSourceParameters("LoquacioHotkeySink")
        {
            Width = 0, Height = 0, PositionX = 0, PositionY = 0,
            WindowStyle = 0, ExtendedWindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_registered.TryGetValue(id, out var binding))
            {
                _logger.LogDebug("Hotkey pressed: {Action} ({Hotkey})", binding.Action, binding);
                HotkeyPressed?.Invoke(this, binding.Action);
                handled = true;
            }
        }
        else if (msg == WM_DESTROY)
        {
            UnregisterAll();
        }

        return IntPtr.Zero;
    }

    public bool Register(HotkeyBinding binding)
    {
        if (_actionToId.ContainsKey(binding.Action))
            Unregister(binding.Action);

        var id = _nextId++;
        var (key, modifiers) = IHotkeyService.ParseHotkeyString(
            IHotkeyService.FormatHotkey(binding.Key, binding.Modifiers));

        if (key == HotkeyKey.None)
        {
            _logger.LogError("Cannot register hotkey with Key.None for action {Action}", binding.Action);
            return false;
        }

        uint fsModifiers = (uint)KeyModifierToWin32(binding.Modifiers);
        uint vk = (uint)HotkeyKeyToVirtualKey(binding.Key);

        if (_source is null)
        {
            _logger.LogError("HwndSource not initialized; cannot register hotkeys");
            return false;
        }

        if (!RegisterHotKey(_source.Handle, id, fsModifiers, vk))
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("Failed to register hotkey {Hotkey} for action {Action}. Win32 error: {Error}",
                binding, binding.Action, error);
            return false;
        }

        _registered[id] = binding;
        _actionToId[binding.Action] = id;
        _logger.LogInformation("Registered hotkey {Hotkey} for action {Action}", binding, binding.Action);
        return true;
    }

    public void Unregister(string action)
    {
        if (!_actionToId.TryGetValue(action, out var id))
            return;

        if (_source is not null)
            UnregisterHotKey(_source.Handle, id);

        _registered.Remove(id);
        _actionToId.Remove(action);
        _logger.LogDebug("Unregistered hotkey for action {Action}", action);
    }

    public void UnregisterAll()
    {
        if (_source is not null)
        {
            foreach (var id in _registered.Keys)
                UnregisterHotKey(_source.Handle, id);
        }

        var count = _registered.Count;
        _registered.Clear();
        _actionToId.Clear();

        if (count > 0)
            _logger.LogInformation("Unregistered all {Count} hotkeys", count);
    }

    public bool IsRegistered(string action) => _actionToId.ContainsKey(action);

    public IReadOnlyList<HotkeyBinding> GetRegisteredHotkeys() => _registered.Values.ToList().AsReadOnly();

    /// <summary>
    /// Convert platform-agnostic HotkeyModifiers to Win32 MOD_* flags.
    /// </summary>
    private static int KeyModifierToWin32(HotkeyModifiers modifiers)
    {
        int result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= 0x0001;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= 0x0002;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= 0x0004;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) result |= 0x0008;
        return result;
    }

    /// <summary>
    /// Convert platform-agnostic HotkeyKey to Win32 virtual key code.
    /// </summary>
    private static uint HotkeyKeyToVirtualKey(HotkeyKey key)
    {
        return key switch
        {
            HotkeyKey.Space => 0x20,
            HotkeyKey.Enter => 0x0D,
            HotkeyKey.Escape => 0x1B,
            HotkeyKey.Tab => 0x09,
            HotkeyKey.Back => 0x08,
            HotkeyKey.Delete => 0x2E,
            HotkeyKey.Insert => 0x2D,
            HotkeyKey.Home => 0x24,
            HotkeyKey.End => 0x23,
            HotkeyKey.PageUp => 0x21,
            HotkeyKey.PageDown => 0x22,
            HotkeyKey.Left => 0x25,
            HotkeyKey.Right => 0x27,
            HotkeyKey.Up => 0x26,
            HotkeyKey.Down => 0x28,
            HotkeyKey.F1 => 0x70,
            HotkeyKey.F2 => 0x71,
            HotkeyKey.F3 => 0x72,
            HotkeyKey.F4 => 0x73,
            HotkeyKey.F5 => 0x74,
            HotkeyKey.F6 => 0x75,
            HotkeyKey.F7 => 0x76,
            HotkeyKey.F8 => 0x77,
            HotkeyKey.F9 => 0x78,
            HotkeyKey.F10 => 0x79,
            HotkeyKey.F11 => 0x7A,
            HotkeyKey.F12 => 0x7B,
            _ => (uint)(0x41 + (key - HotkeyKey.A)) // A-Z and D0-D9 map directly
        };
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
        GC.SuppressFinalize(this);
    }

    ~HotkeyService() => Dispose();
}
