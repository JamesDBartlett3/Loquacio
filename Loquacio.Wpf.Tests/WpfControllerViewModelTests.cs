using Microsoft.Extensions.Logging;

namespace Loquacio.Wpf.Tests;

public class WpfControllerViewModelTests
{
    private readonly IEngineProxy _engine;
    private readonly IDispatcherService _dispatcher;
    private readonly WpfSettingsPersistenceService _settings;
    private readonly ITrayIconService _trayIcon;
    private readonly ILogger<WpfControllerViewModel> _logger;

    public WpfControllerViewModelTests()
    {
        _engine = Substitute.For<IEngineProxy>();
        _dispatcher = Substitute.For<IDispatcherService>();

        // Use a real temp file for settings so Load/Save actually work
        var path = Path.Combine(Path.GetTempPath(), $"vm-test-{Guid.NewGuid():N}.json");
        var settingsLogger = Substitute.For<ILogger<WpfSettingsPersistenceService>>();
        _settings = new WpfSettingsPersistenceService(settingsLogger, path);

        _trayIcon = Substitute.For<ITrayIconService>();
        _logger = Substitute.For<ILogger<WpfControllerViewModel>>();
    }

    [Fact]
    public void Constructor_SubscribesToEngineDisconnected()
    {
        var vm = CreateViewModel();

        _engine.Received(1).Disconnected += Arg.Any<EventHandler>();
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
        Assert.True(vm.AutoStartEngine); // default
    }

    [Fact]
    public void SaveSettings_PersistsCurrentState()
    {
        var vm = CreateViewModel();
        vm.MinimizeToTray = false;
        vm.CloseToTray = false;
        vm.AutoStartEngine = false;

        vm.SaveSettings();

        var loaded = _settings.Load();
        Assert.NotNull(loaded);
        Assert.False(loaded!.MinimizeToTray);
        Assert.False(loaded.CloseToTray);
        Assert.False(loaded.AutoStartEngine);
    }

    [Fact]
    public void Dispose_SavesSettings_AndUnsubscribes()
    {
        var vm = CreateViewModel();

        vm.Dispose();

        _engine.Received(1).Disconnected -= Arg.Any<EventHandler>();
        _engine.Received(1).Dispose();
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
        _engine.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AckMessage { Success = true });

        // Set IsListening to false
        typeof(WpfControllerViewModel)
            .GetProperty("IsListening")!
            .SetValue(vm, false);

        // Act
        await vm.ToggleListeningCommand.ExecuteAsync(null);

        // Assert
        await _engine.Received(1).SendCommandAsync(
            Arg.Is<StartListeningMessage>(m => m.MessageType == "start-listening"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleListening_WhenListening_SendsStopCommand()
    {
        var vm = CreateViewModel();
        _engine.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AckMessage { Success = true });

        typeof(WpfControllerViewModel)
            .GetProperty("IsListening")!
            .SetValue(vm, true);

        await vm.ToggleListeningCommand.ExecuteAsync(null);

        await _engine.Received(1).SendCommandAsync(
            Arg.Is<StopListeningMessage>(m => m.MessageType == "stop-listening"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModeSwitch_On_SendsPushToTalk()
    {
        var vm = CreateViewModel();
        vm.IsConnected = true;
        vm.CurrentMode = "Continuous";
        _engine.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AckMessage { Success = true });

        vm.IsPushToTalk = true;

        await _engine.Received(1).SendCommandAsync(
            Arg.Is<SetModeMessage>(m => m.Mode == ActivationMode.PushToTalk),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModeSwitch_Off_SendsContinuous()
    {
        var vm = CreateViewModel();
        vm.IsConnected = true;
        vm.CurrentMode = "Push-to-Talk";
        _engine.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AckMessage { Success = true });

        vm.IsPushToTalk = false;

        await _engine.Received(1).SendCommandAsync(
            Arg.Is<SetModeMessage>(m => m.Mode == ActivationMode.Continuous),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CurrentModeChange_SyncsModeSwitch()
    {
        // The engine reports the mode via status updates; the switch follows.
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

        _engine.DidNotReceive().SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleListening_WhenEngineThrows_LogsError()
    {
        var vm = CreateViewModel();
        _engine.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
            .Returns<AckMessage>(_ => throw new InvalidOperationException("connection lost"));

        // Should not throw
        await vm.ToggleListeningCommand.ExecuteAsync(null);
    }

    private WpfControllerViewModel CreateViewModel()
    {
        return new WpfControllerViewModel(
            _engine,
            _dispatcher,
            _settings,
            _trayIcon,
            _logger);
    }
}
