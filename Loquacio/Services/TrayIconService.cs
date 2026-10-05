using System.Drawing;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Loquacio.Models;

namespace Loquacio.Services;

/// <summary>
/// System tray icon service using Hardcodet.NotifyIcon.Wpf.
/// Provides context menu, notifications, and status indicators.
/// </summary>
public class TrayIconService : ITrayIconService
{
    private TaskbarIcon? _notifyIcon;
    private bool _disposed;

    // Icon resources keyed by state - loaded from embedded resources
    private readonly Dictionary<TrayIconState, string> _stateIcons = new()
    {
        { TrayIconState.Idle, "pack://application:,,,/Resources/tray_icon_idle.ico" },
        { TrayIconState.Listening, "pack://application:,,,/Resources/tray_icon_listening.ico" },
        { TrayIconState.Processing, "pack://application:,,,/Resources/tray_icon_processing.ico" },
        { TrayIconState.Error, "pack://application:,,,/Resources/tray_icon_error.ico" }
    };

    private readonly Dictionary<TrayIconState, string> _stateTooltips = new()
    {
        { TrayIconState.Idle, "Loquacio - Idle" },
        { TrayIconState.Listening, "Loquacio - Listening" },
        { TrayIconState.Processing, "Loquacio - Processing" },
        { TrayIconState.Error, "Loquacio - Error" }
    };

    public event EventHandler? ShowRequested;
    public event EventHandler? ToggleListeningRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;

    public void Initialize()
    {
        if (_notifyIcon != null) return;

        _notifyIcon = new TaskbarIcon
        {
            ToolTipText = _stateTooltips[TrayIconState.Idle],
            Visibility = Visibility.Visible
        };

        // Load the idle icon from embedded resources
        try
        {
            var iconUri = new Uri(_stateIcons[TrayIconState.Idle]);
            var iconStream = Application.GetResourceStream(iconUri);
            if (iconStream != null)
            {
                _notifyIcon.Icon = new Icon(iconStream.Stream);
            }
        }
        catch
        {
            // Fall back to a default system icon if custom icon unavailable
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _notifyIcon.DoubleClickCommand = new SimpleCommand(_ => ShowRequested?.Invoke(this, EventArgs.Empty));

        // Attach the context menu so right-click works
        _notifyIcon.ContextMenu = BuildContextMenu(isListening: false);
    }

    public void Show()
    {
        if (_notifyIcon != null)
            _notifyIcon.Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        if (_notifyIcon != null)
            _notifyIcon.Visibility = Visibility.Hidden;
    }

    public void UpdateTooltip(string tooltip)
    {
        if (_notifyIcon != null)
            _notifyIcon.ToolTipText = tooltip;
    }

    public void UpdateState(TrayIconState state)
    {
        if (_notifyIcon == null) return;

        UpdateTooltip(_stateTooltips.GetValueOrDefault(state, "Loquacio"));

        // Refresh context menu to reflect current listening state
        var isListening = state == TrayIconState.Listening;
        _notifyIcon.ContextMenu = BuildContextMenu(isListening);

        // Load state-specific icon from embedded resources
        try
        {
            var iconUri = new Uri(_stateIcons.GetValueOrDefault(state, _stateIcons[TrayIconState.Idle]));
            var iconStream = Application.GetResourceStream(iconUri);
            if (iconStream != null)
            {
                _notifyIcon.Icon = new Icon(iconStream.Stream);
            }
        }
        catch
        {
            // Fall back to system icon
            _notifyIcon.Icon = state switch
            {
                TrayIconState.Listening => SystemIcons.Information,
                TrayIconState.Processing => SystemIcons.Warning,
                TrayIconState.Error => SystemIcons.Error,
                _ => SystemIcons.Application
            };
        }
    }

    public void ShowNotification(string title, string message, int timeout = 0)
    {
        _notifyIcon?.ShowBalloonTip(title, message, BalloonIcon.Info);
    }

    /// <summary>
    /// Builds the context menu for the tray icon.
    /// Called by the XAML renderer when the tray icon is right-clicked.
    /// </summary>
    public System.Windows.Controls.ContextMenu BuildContextMenu(bool isListening)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem
        {
            Header = "Show Window",
            FontWeight = FontWeights.SemiBold
        };
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(showItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var toggleItem = new System.Windows.Controls.MenuItem
        {
            Header = isListening ? "Stop Listening" : "Start Listening"
        };
        toggleItem.Click += (_, _) => ToggleListeningRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(toggleItem);

        var settingsItem = new System.Windows.Controls.MenuItem
        {
            Header = "Settings..."
        };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem
        {
            Header = "Exit"
        };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        return menu;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Simple ICommand implementation for tray icon command bindings.
/// </summary>
internal sealed class SimpleCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?> _execute;

    public SimpleCommand(Action<object?> execute) => _execute = execute;

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
