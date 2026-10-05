using Microsoft.Extensions.Logging;
using WhisperDictation.Models;

namespace WhisperDictation.Daemon.Services;

/// <summary>
/// No-op <see cref="IHotkeyService"/> for the daemon context.
///
/// In the daemon, global (system-wide) hotkeys are handled by <see cref="HotkeyManager"/>,
/// which is platform-aware (X11 XGrabKey on Linux, RegisterHotKey on Windows) and
/// configurable via appsettings.json. The legacy IHotkeyService path used by
/// ActivationManagerService is a leftover from the monolithic WPF architecture;
/// registering this no-op keeps the DI graph resolvable without double-registering
/// system hotkeys through two competing mechanisms.
/// </summary>
public sealed class NullHotkeyService(ILogger<NullHotkeyService> logger) : IHotkeyService
{
    public event EventHandler<string>? HotkeyPressed
    {
        add { }
        remove { }
    }

    public bool Register(HotkeyBinding binding)
    {
        logger.LogDebug("Ignoring hotkey registration for '{Action}' — daemon hotkeys are managed by HotkeyManager", binding.Action);
        return false;
    }

    public void Unregister(string action) { }

    public void UnregisterAll() { }

    public bool IsRegistered(string action) => false;

    public IReadOnlyList<HotkeyBinding> GetRegisteredHotkeys() => [];

    public void Dispose() { }
}
