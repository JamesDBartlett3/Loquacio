using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Loquacio.Avalonia.Services;

/// <summary>
/// System tray integration for the Avalonia controller.
/// Manages tray icon with context menu for quick actions.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly ILogger<TrayService> _logger;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _toggleItem;
    private bool _disposed;

    public TrayService(ILogger<TrayService> logger)
    {
        _logger = logger;
    }

    public void Initialize(Window mainWindow, Action toggleListening, Action showWindow, Action quitApp)
    {
        var menu = new NativeMenu();

        _toggleItem = new NativeMenuItem("▶ Start Dictation");
        _toggleItem.Click += (_, _) => toggleListening();
        menu.Add(_toggleItem);

        var showItem = new NativeMenuItem("Show Window");
        showItem.Click += (_, _) => showWindow();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => quitApp();
        menu.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            Menu = menu,
            ToolTipText = "Loquacio",
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => showWindow();

        _logger.LogInformation("System tray icon initialized");
    }

    public void UpdateListeningState(bool isListening)
    {
        if (_toggleItem != null)
        {
            _toggleItem.Header = isListening ? "⏹ Stop Dictation" : "▶ Start Dictation";
        }

        if (_trayIcon != null)
        {
            _trayIcon.ToolTipText = isListening ? "Loquacio — Listening" : "Loquacio — Idle";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon = null;
        }
        GC.SuppressFinalize(this);
    }
}
