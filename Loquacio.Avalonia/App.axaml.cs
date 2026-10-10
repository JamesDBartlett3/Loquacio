using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Loquacio.Avalonia.Services;
using Loquacio.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Loquacio.Avalonia;

public class App : Application
{
    private IServiceProvider? _services;
    private TrayService? _trayService;
    private TrayIconViewModel? _trayVm;
    private ControllerViewModel? _vm;
    private IEngineLifecycleService? _engineLifecycle;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = ConfigureServices();
        _vm = _services.GetRequiredService<ControllerViewModel>();
        _trayService = _services.GetRequiredService<TrayService>();
        _trayVm = _services.GetRequiredService<TrayIconViewModel>();
        _engineLifecycle = _services.GetRequiredService<IEngineLifecycleService>();

        // Load persisted settings before showing window
        _vm.LoadSettings();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = _services.GetRequiredService<Views.MainWindow>();
            mainWindow.DataContext = _vm;

            // Phase 6.2 — Minimize-to-tray:
            // 1) Closing the window hides it instead of shutting down.
            mainWindow.Closing += (_, e) =>
            {
                if (_vm.MinimizeToTray)
                {
                    e.Cancel = true;
                    mainWindow.Hide();
                }
            };

            // 2) Minimizing the window hides it too (true tray behavior).
            mainWindow.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.WindowStateProperty &&
                    mainWindow.WindowState == WindowState.Minimized &&
                    _vm.MinimizeToTray)
                {
                    mainWindow.Hide();
                }
            };

            desktop.MainWindow = mainWindow;

            // Phase 6.1 — Tray icon via TrayIconViewModel
            _trayVm = new TrayIconViewModel(
                async () =>
                {
                    await _vm.ToggleListeningCommand.ExecuteAsync(null);
                    _trayVm.UpdateState(_vm.IsListening, _vm.IsConnected, _vm.StatusText);
                },
                () =>
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                },
                () =>
                {
                    _vm.SaveSettings();
                    _vm.Dispose();
                    _engineLifecycle.Dispose();
                    _trayService.Dispose();
                    desktop.Shutdown();
                },
                _services.GetRequiredService<ILogger<TrayIconViewModel>>());

            _trayService.Initialize(_trayVm, mainWindow);

            // Watch controller state for tray updates
            void SyncTray() => _trayVm.UpdateState(_vm.IsListening, _vm.IsConnected, _vm.StatusText);
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ControllerViewModel.IsListening)
                    or nameof(ControllerViewModel.IsConnected)
                    or nameof(ControllerViewModel.StatusText))
                {
                    SyncTray();
                }
            };
            SyncTray();

            // Start engine lifecycle: ensure engine is running, then connect
            _ = Task.Run(async () =>
            {
                await _vm.InitializeAsync(_engineLifecycle);

                // Start health monitoring after initial connection
                if (_engineLifecycle.State == EngineState.Running)
                {
                    _engineLifecycle.StartHealthMonitoring();

                    // When engine health check fails, update UI
                    _engineLifecycle.HealthCheckFailed += (_, _) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _vm.ConnectionStatus = "Engine unhealthy";
                            _vm.StatusText = "Reconnecting…";
                        });
                    };
                }

                Dispatcher.UIThread.Post(SyncTray);
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
        services.AddSingleton<IEngineProxy, EngineProxy>();

        // Dispatcher
        services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();

        // Settings persistence
        services.AddSingleton<SettingsPersistenceService>();

        // System tray
        services.AddSingleton<TrayService>();
        services.AddSingleton(sp => new TrayIconViewModel(
            () => Task.CompletedTask, () => { }, () => { },
            sp.GetRequiredService<ILogger<TrayIconViewModel>>()));

        // Phase 6.3 — auto-start with Windows (HKCU Run key)
        services.AddSingleton<Loquacio.Services.IAutoStartService, Loquacio.Services.RegistryAutoStartService>();

        // Engine lifecycle management
        services.AddSingleton<IEngineLifecycleService, EngineLifecycleService>();

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
