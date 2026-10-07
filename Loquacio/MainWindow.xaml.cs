using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Loquacio.Services;
using Loquacio.ViewModels;

namespace Loquacio;

public partial class MainWindow : Window
{
    private readonly WpfControllerViewModel _viewModel;
    private readonly ITrayIconService _trayIconService;
    private readonly WpfSettingsPersistenceService _settingsService;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        _viewModel = app.Services.GetRequiredService<WpfControllerViewModel>();
        _trayIconService = app.Services.GetRequiredService<ITrayIconService>();
        _settingsService = app.Services.GetRequiredService<WpfSettingsPersistenceService>();

        DataContext = _viewModel;

        Closed += OnWindowClosed;
        StateChanged += OnStateChanged;
        Closing += OnWindowClosing;

        // Wire up tray icon events to controller commands
        _trayIconService.ShowRequested += OnTrayShowRequested;
        _trayIconService.ToggleListeningRequested += async (_, _) =>
        {
            await _viewModel.ToggleListeningCommand.ExecuteAsync(null);
        };
        _trayIconService.ToggleModeRequested += (_, _) =>
        {
            // Flipping the switch sends SetMode to the daemon, which confirms
            // via a status broadcast that re-syncs the menu.
            _viewModel.IsPushToTalk = !_viewModel.IsPushToTalk;
        };
        _trayIconService.ToggleLlmRequested += (_, _) =>
        {
            if (_viewModel.LlmSettings is { } llm)
                llm.IsLlmEnabled = !llm.IsLlmEnabled; // auto-applies and pushes to the daemon
        };
        _trayIconService.ExitRequested += OnTrayExitRequested;
        _trayIconService.SettingsRequested += OnTraySettingsRequested;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel.MinimizeToTray)
        {
            Hide();
            _trayIconService.UpdateState(TrayIconState.Idle);
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_forceClose && _viewModel.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIconService.UpdateState(TrayIconState.Idle);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // ViewModel disposal is handled by App.OnExit
    }

    /// <summary>Dashboard banner button — jump to the Whisper panel (sidebar index 3).</summary>
    private void OnOpenWhisperSettings(object sender, RoutedEventArgs e)
    {
        if (FindName("NavList") is System.Windows.Controls.ListBox nav)
            nav.SelectedIndex = 3;
    }

    private void OnTrayShowRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _trayIconService.UpdateState(_viewModel.IsListening ? TrayIconState.Listening : TrayIconState.Idle);
        });
    }

    /// <summary>Tray "Settings…" — show the window and open the Activation & Hotkeys tab.</summary>
    private void OnTraySettingsRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (FindName("NavList") is System.Windows.Controls.ListBox nav)
                nav.SelectedIndex = 4;
        });
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        _forceClose = true;
        Dispatcher.Invoke(() =>
        {
            _trayIconService.Hide();
            Close();
        });
    }
}
