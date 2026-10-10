using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Loquacio.Avalonia.ViewModels;
using Microsoft.Extensions.Logging;

namespace Loquacio.Avalonia.Services;

/// <summary>
/// System tray integration for the Avalonia controller.
/// Renders the TrayIcon described by TrayIconViewModel and keeps its
/// menu labels / tooltip in sync with controller state.
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

    public void Initialize(TrayIconViewModel vm, Window mainWindow)
    {
        var menu = new NativeMenu();

        _toggleItem = new NativeMenuItem("▶ Start Dictation");
        // NativeMenuItem.Click is sync; post through the dispatcher so the
        // async toggle command runs on the UI thread.
        _toggleItem.Click += (_, _) => Dispatcher.UIThread.Post(() => vm.ToggleListeningCommand.Execute(null));
        menu.Add(_toggleItem);

        var showItem = new NativeMenuItem("Show Window");
        showItem.Click += (_, _) => Dispatcher.UIThread.Post(() => vm.ShowWindowCommand.Execute(null));
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Dispatcher.UIThread.Post(() => vm.QuitCommand.Execute(null));
        menu.Add(quitItem);

        using var iconStream = AssetLoader.Open(
            new Uri("avares://Loquacio.Avalonia/Assets/loquacio-icon.png"));

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            Menu = menu,
            ToolTipText = vm.ToString(),
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => Dispatcher.UIThread.Post(() => vm.ShowWindowCommand.Execute(null));

        // Keep tray tooltip / menu label in sync with the ViewModel
        vm.PropertyChanged += (_, e) =>
        {
            if (_trayIcon == null) return;
            if (e.PropertyName == nameof(TrayIconViewModel.IsListening))
            {
                _toggleItem!.Header = vm.IsListening ? "⏹ Stop Dictation" : "▶ Start Dictation";
            }
            if (e.PropertyName == nameof(TrayIconViewModel.StatusText))
            {
                _trayIcon.ToolTipText = vm.ToString();
            }
        };

        _logger.LogInformation("System tray icon initialized");
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
