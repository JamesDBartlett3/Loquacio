using Microsoft.Extensions.Logging;

namespace Loquacio.Wpf.Tests;

public class WpfControllerViewModelTests
{
    private readonly IDaemonProxy _daemon;
    private readonly IDispatcherService _dispatcher;
    private readonly WpfSettingsPersistenceService _settings;
    private readonly ITrayIconService _trayIcon;
    private readonly ILogger<WpfControllerViewModel> _logger;

    public WpfControllerViewModelTests()
    {
        _daemon = Substitute.For<IDaemonProxy>();
        _dispatcher = Substitute.For<IDispatcherService>();

        // Use a real temp file for settings so Load/Save actually work
        var path = Path.Combine(Path.GetTempPath(), $"vm-test-{Guid.NewGuid():N}.json");
        var settingsLogger = Substitute.For<ILogger<WpfSettingsPersistenceService>>();
        _settings = new WpfSettingsPersistenceService(settingsLogger, path);

        _trayIcon = Substitute.For<ITrayIconService>();
        _logger = Substitute.For<ILogger<WpfControllerViewModel>>();
    }

    [Fact]
    public void Constructor_SubscribesToDaemonDisconnected()
    {
        var vm = CreateViewModel();

        _daemon.Received(1).Disconnected += Arg.Any<EventHandler>();
    }

    [Fact]
    public void Constructor_DefaultState_IsDisconnected()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsConnected);
        Assert.Equal("Disconnected", vm.ConnectionStatus);
        Assert.Equal("Connecting…", vm.StatusText);
    }

    [Fact]
    public void LoadSettings_NoFile_UsesDefaults()
    {
        var vm = CreateViewModel();

        vm.LoadSettings();

        Assert.True(vm.MinimizeToTray); // default
        Assert.True(vm.CloseToTray); // default
        Assert.True(vm.AutoStartDaemon); // default
    }

    [Fact]
    public void SaveSettings_PersistsCurrentState()
    {
        var vm = CreateViewModel();
        vm.MinimizeToTray = false;
        vm.CloseToTray = false;
        vm.AutoStartDaemon = false;

        vm.SaveSettings();

        var loaded = _settings.Load();
        Assert.NotNull(loaded);
        Assert.False(loaded!.MinimizeToTray);
        Assert.False(loaded.CloseToTray);
        Assert.False(loaded.AutoStartDaemon);
    }

    [Fact]
    public void Dispose_SavesSettings_AndUnsubscribes()
    {
        var vm = CreateViewModel();

        vm.Dispose();

        _daemon.Received(1).Disconnected -= Arg.Any<EventHandler>();
        _daemon.Received(1).Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var vm = CreateViewModel();

        vm.Dispose();
        vm.Dispose(); // should be idempotent
    }

    [Fact]
    public async Task ToggleListening_WhenNotListening_SendsStartCommand()
    {
        // Arrange: ViewModel starts not listening
        var vm = CreateViewModel();
        _daemon.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AckMessage { Success = true });

        // Set IsListening to false
        typeof(WpfControllerViewModel)
            .GetProperty("IsListening")!
            .SetValue(vm, false);

        // Act
        await vm.ToggleListeningCommand.ExecuteAsync(null);

        // Assert
        await _daemon.Received(1).SendCommandAsync(
            Arg.Is<StartListeningMessage>(m => m.MessageType == "start-listening"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleListening_WhenListening_SendsStopCommand()
    {
        var vm = CreateViewModel();
        _daemon.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AckMessage { Success = true });

        typeof(WpfControllerViewModel)
            .GetProperty("IsListening")!
            .SetValue(vm, true);

        await vm.ToggleListeningCommand.ExecuteAsync(null);

        await _daemon.Received(1).SendCommandAsync(
            Arg.Is<StopListeningMessage>(m => m.MessageType == "stop-listening"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModeSwitch_On_SendsPushToTalk()
    {
        var vm = CreateViewModel();
        vm.IsConnected = true;
        vm.CurrentMode = "Continuous";
        _daemon.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AckMessage { Success = true });

        vm.IsPushToTalk = true;

        await _daemon.Received(1).SendCommandAsync(
            Arg.Is<SetModeMessage>(m => m.Mode == ActivationMode.PushToTalk),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModeSwitch_Off_SendsContinuous()
    {
        var vm = CreateViewModel();
        vm.IsConnected = true;
        vm.CurrentMode = "Push-to-Talk";
        _daemon.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AckMessage { Success = true });

        vm.IsPushToTalk = false;

        await _daemon.Received(1).SendCommandAsync(
            Arg.Is<SetModeMessage>(m => m.Mode == ActivationMode.Continuous),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CurrentModeChange_SyncsModeSwitch()
    {
        // The daemon reports the mode via status updates; the switch follows.
        var vm = CreateViewModel();

        vm.CurrentMode = "Push-to-Talk";
        Assert.True(vm.IsPushToTalk);

        vm.CurrentMode = "Continuous";
        Assert.False(vm.IsPushToTalk);
    }

    [Fact]
    public void ModeSwitch_NotConnected_DoesNotSendCommand()
    {
        var vm = CreateViewModel();

        vm.IsPushToTalk = true;

        _daemon.DidNotReceive().SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleListening_WhenDaemonThrows_LogsError()
    {
        var vm = CreateViewModel();
        _daemon.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns<AckMessage>(_ => throw new InvalidOperationException("connection lost"));

        // Should not throw
        await vm.ToggleListeningCommand.ExecuteAsync(null);
    }

    private WpfControllerViewModel CreateViewModel()
    {
        return new WpfControllerViewModel(
            _daemon,
            _dispatcher,
            _settings,
            _trayIcon,
            _logger);
    }
}
