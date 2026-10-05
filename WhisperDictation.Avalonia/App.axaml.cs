using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WhisperDictation.Avalonia.Services;
using WhisperDictation.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace WhisperDictation.Avalonia;

public class App : Application
{
    private IServiceProvider? _services;
    private TrayService? _trayService;
    private ControllerViewModel? _vm;
    private IDaemonLifecycleService? _daemonLifecycle;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = ConfigureServices();
        _vm = _services.GetRequiredService<ControllerViewModel>();
        _trayService = _services.GetRequiredService<TrayService>();
        _daemonLifecycle = _services.GetRequiredService<IDaemonLifecycleService>();

        // Load persisted settings before showing window
        _vm.LoadSettings();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = _services.GetRequiredService<Views.MainWindow>();
            mainWindow.DataContext = _vm;

            // Minimize-to-tray: hide window instead of closing
            mainWindow.Closing += (_, e) =>
            {
                if (_vm.MinimizeToTray)
                {
                    e.Cancel = true;
                    mainWindow.Hide();
                }
            };

            desktop.MainWindow = mainWindow;

            // Initialize tray icon
            _trayService.Initialize(
                mainWindow,
                () => Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await _vm.ToggleListeningCommand.ExecuteAsync(null);
                    _trayService.UpdateListeningState(_vm.IsListening);
                }),
                () =>
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                },
                () =>
                {
                    _vm.SaveSettings();
                    _vm.Dispose();
                    _daemonLifecycle.Dispose();
                    _trayService.Dispose();
                    desktop.Shutdown();
                });

            // Watch listening state for tray updates
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ControllerViewModel.IsListening))
                    _trayService.UpdateListeningState(_vm.IsListening);
            };

            // Start daemon lifecycle: ensure daemon is running, then connect
            _ = Task.Run(async () =>
            {
                await _vm.InitializeAsync(_daemonLifecycle);

                // Start health monitoring after initial connection
                if (_daemonLifecycle.State == DaemonState.Running)
                {
                    _daemonLifecycle.StartHealthMonitoring();

                    // When daemon health check fails, update UI
                    _daemonLifecycle.HealthCheckFailed += (_, _) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _vm.ConnectionStatus = "Daemon unhealthy";
                            _vm.StatusText = "Reconnecting…";
                        });
                    };
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        }).SetMinimumLevel(LogLevel.Debug));

        // IPC
        services.AddSingleton<IDaemonProxy, DaemonProxy>();

        // Dispatcher
        services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();

        // Settings persistence
        services.AddSingleton<SettingsPersistenceService>();

        // System tray
        services.AddSingleton<TrayService>();

        // Daemon lifecycle management
        services.AddSingleton<IDaemonLifecycleService, DaemonLifecycleService>();

        // Linux platform services (auto-selects between real and null implementation)
        if (OperatingSystem.IsLinux())
            services.AddSingleton<ILinuxPlatformService, LinuxPlatformService>();
        else
            services.AddSingleton<ILinuxPlatformService, NullLinuxPlatformService>();

        // ViewModels
        services.AddSingleton<ControllerViewModel>();
        services.AddSingleton<GeneralSettingsViewModel>();
        services.AddSingleton<AudioSettingsViewModel>();
        services.AddSingleton<WhisperSettingsViewModel>();
        services.AddSingleton<LLMSettingsViewModel>();
        services.AddSingleton<VocabularySettingsViewModel>();

        // Views
        services.AddSingleton<Views.MainWindow>();

        return services.BuildServiceProvider();
    }
}
