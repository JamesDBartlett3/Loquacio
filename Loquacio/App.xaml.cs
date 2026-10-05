using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Loquacio.Services;
using Loquacio.ViewModels;

namespace Loquacio;

public partial class App : Application
{
    public IServiceProvider Services { get; }
    private MainWindow? _mainWindow;
    private ITrayIconService? _trayIconService;
    private ISingleInstanceService? _singleInstanceService;
    private WpfControllerViewModel? _controllerViewModel;
    private WpfDaemonLifecycleService? _daemonLifecycle;

    public App()
    {
        Services = ConfigureServices();
    }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        _singleInstanceService = Services.GetRequiredService<ISingleInstanceService>();
        if (!_singleInstanceService.TryAcquireLock())
        {
            Shutdown();
            return;
        }

        _trayIconService = Services.GetRequiredService<ITrayIconService>();
        _trayIconService.Initialize();

        _controllerViewModel = Services.GetRequiredService<WpfControllerViewModel>();
        _daemonLifecycle = Services.GetService<WpfDaemonLifecycleService>();

        // Wire the settings-tab VMs into the controller (tabs bind to these;
        // saves are pushed to the daemon via IPC)
        _controllerViewModel.AttachSettingsViewModels(
            Services.GetRequiredService<AudioTabViewModel>(),
            Services.GetRequiredService<WhisperTabViewModel>(),
            Services.GetRequiredService<GeneralTabViewModel>(),
            Services.GetRequiredService<VocabularyTabViewModel>(),
            Services.GetRequiredService<LLMTabViewModel>());

        // Load persisted controller settings
        _controllerViewModel.LoadSettings();

        _mainWindow = new MainWindow();

        var args = e.Args;
        if (args.Contains("--minimized") || _controllerViewModel.StartMinimized)
        {
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.WindowState = WindowState.Minimized;
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.Show();
        }

        // Initialize daemon connection asynchronously
        await _controllerViewModel.InitializeAsync();
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        // The UI and the background service launch and quit together.
        if (_controllerViewModel is not null)
        {
            try { await _controllerViewModel.ShutdownAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"Failed to stop background service on exit: {ex.Message}"); }
            _controllerViewModel.Dispose();
        }
        _trayIconService?.Hide();
        _trayIconService?.Dispose();
        _singleInstanceService?.Dispose();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(configure =>
        {
            configure.AddConsole();
            configure.SetMinimumLevel(LogLevel.Debug);
        });

        // Dispatcher (WPF implementation)
        services.AddSingleton<IDispatcherService, WpfDispatcherService>();

        // --- Daemon Controller Architecture (Phase C2) ---
        // The WPF app is now a thin controller that connects to the daemon via IPC.
        // It no longer owns the audio/whisper pipeline directly.

        // IPC: Auto-detects named pipe (Windows) or Unix socket (Linux)
        services.AddSingleton<Loquacio.Ipc.IDaemonProxy>(sp =>
            new Loquacio.Ipc.DaemonProxy(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Loquacio.Ipc.DaemonProxy>>()));

        // Daemon lifecycle management (start, monitor, restart)
        services.AddSingleton<WpfDaemonLifecycleService>();

        // In-process daemon option (hosts the daemon inside this process)
        services.AddSingleton<Loquacio.Daemon.Services.InProcessDaemonHost>();

        // Controller settings persistence
        services.AddSingleton<WpfSettingsPersistenceService>();

        // --- Settings tabs (daemon-backed settings, shared store + IPC push) ---
        // The controller mirrors the daemon's settings store on the same machine and
        // pushes changes via IPC update-settings so they take effect in the daemon.
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IModelManagerService, ModelManagerService>();

        // Device enumeration only — the daemon owns actual capture. Using WASAPI
        // enumeration here is read-only and doesn't conflict with the daemon.
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IAudioCaptureService, Loquacio.Daemon.Services.Audio.WasapiAudioCaptureService>();
        }
        else
        {
            services.AddSingleton<IAudioCaptureService, Loquacio.Daemon.Services.UnsupportedAudioCaptureService>();
        }

        // Global hotkeys are owned by the daemon's HotkeyManager — the controller
        // must not double-register system hotkeys. This no-op only satisfies the
        // GeneralTabViewModel dependency.
        services.AddSingleton<IHotkeyService, Loquacio.Daemon.Services.NullHotkeyService>();

        services.AddSingleton<AudioTabViewModel>();
        services.AddSingleton<WhisperTabViewModel>();
        services.AddSingleton<GeneralTabViewModel>();
        services.AddSingleton<VocabularyTabViewModel>();
        services.AddSingleton<IVocabularyService, VocabularyService>();
        services.AddSingleton<ILLMPostProcessorService, LLMPostProcessorService>();
        services.AddSingleton<LLMTabViewModel>();

        // Tray icon (wired to daemon commands via ControllerViewModel)
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<ISingleInstanceService, SingleInstanceService>();

        // Auto-start service (Windows registry)
        services.AddSingleton<IAutoStartService, AutoStartService>();

        // Main controller ViewModel (replaces old MainWindowViewModel)
        services.AddSingleton<WpfControllerViewModel>();

        return services.BuildServiceProvider();
    }
}
