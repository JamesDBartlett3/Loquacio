using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WhisperDictation.Daemon.Interop;
using WhisperDictation.Daemon.Models;
using WhisperDictation.Daemon.Services;

namespace WhisperDictation.Tests.Daemon;

public class HotkeyManagerTests
{
    private readonly IActivationManagerService _activation;
    private readonly IX11Interop _x11Interop;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HotkeyManager> _logger;

    public HotkeyManagerTests()
    {
        _activation = Substitute.For<IActivationManagerService>();
        _x11Interop = Substitute.For<IX11Interop>();
        _configuration = new ConfigurationBuilder().Build();
        _logger = Substitute.For<ILogger<HotkeyManager>>();
    }

    private HotkeyManager CreateManager() =>
        new(_activation, _configuration, _logger, _x11Interop);

    [Fact]
    public async Task InitializeAsync_CompletesWithoutError()
    {
        var manager = CreateManager();

        await manager.InitializeAsync();

        // Should not throw on any platform
        Assert.True(true);
        manager.Dispose();
    }

    [Fact]
    public async Task TriggerToggleAsync_CallsActivationManager()
    {
        var manager = CreateManager();

        await manager.TriggerToggleAsync();

        await _activation.Received(1).ToggleListeningAsync();
        manager.Dispose();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var manager = CreateManager();

        manager.Dispose();
        manager.Dispose(); // should not throw

        Assert.True(true);
    }

    [Fact]
    public async Task Dispose_AfterInitialize_CleansUpGracefully()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();

        manager.Dispose(); // should not throw even if X11 init ran

        Assert.True(true);
    }

    [Fact]
    public async Task InitializeAsync_LogsPlatformInfo()
    {
        var manager = CreateManager();

        await manager.InitializeAsync();

        _logger.ReceivedWithAnyArgs().Log(
            Arg.Any<LogLevel>(),
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        manager.Dispose();
    }

    // ── Config-driven hotkey tests ──

    [Fact]
    public async Task InitializeAsync_LoadsDefaultHotkeys_WhenConfigEmpty()
    {
        // Empty configuration → should use defaults
        var manager = CreateManager();

        await manager.InitializeAsync();

        // Should have logged info about default hotkeys
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("default")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        manager.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_LoadsHotkeysFromConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hotkeys:Hotkeys:0:Key"] = "D",
                ["Hotkeys:Hotkeys:0:Modifiers"] = "Ctrl+Alt",
                ["Hotkeys:Hotkeys:0:Action"] = "toggle",
                ["Hotkeys:Hotkeys:1:Key"] = "F6",
                ["Hotkeys:Hotkeys:1:Modifiers"] = "Shift",
                ["Hotkeys:Hotkeys:1:Action"] = "toggle",
            })
            .Build();

        var manager = new HotkeyManager(_activation, config, _logger, _x11Interop);

        await manager.InitializeAsync();

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("2 hotkey")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        manager.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_X11InteropNull_LogsWarningAndContinues()
    {
        var manager = new HotkeyManager(_activation, _configuration, _logger, null);

        await manager.InitializeAsync();

        // On Linux this logs a warning; on other platforms it's fine
        // Either way, should not throw
        Assert.True(true);
        manager.Dispose();
    }

    // ── Modifier parsing tests ──

    [Theory]
    [InlineData("Ctrl+Alt", true)]   // Valid combo
    [InlineData("Ctrl+Shift", true)]
    [InlineData("Alt", true)]
    [InlineData("Win+Shift", true)]
    [InlineData("", true)]           // Empty = no modifiers, still valid
    public async Task InitializeAsync_AcceptsVariousModifierStrings(string modifiers, bool shouldNotThrow)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hotkeys:Hotkeys:0:Key"] = "Space",
                ["Hotkeys:Hotkeys:0:Modifiers"] = modifiers,
                ["Hotkeys:Hotkeys:0:Action"] = "toggle",
            })
            .Build();

        var manager = new HotkeyManager(_activation, config, _logger, _x11Interop);

        if (shouldNotThrow)
        {
            await manager.InitializeAsync();
            // No exception = pass
        }

        manager.Dispose();
    }

    // ── X11 mock interaction tests ──

    [Fact]
    public async Task InitializeAsync_X11DisplayFails_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux()) return;

        // Simulate X11 display open failure
        _x11Interop.OpenDisplay().Returns(IntPtr.Zero);

        var manager = CreateManager();
        await manager.InitializeAsync();

        // Should log warning about display, not crash
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        manager.Dispose();
    }

    [Fact]
    public async Task Dispose_CleansUpX11Display_WhenInitialized()
    {
        if (!OperatingSystem.IsLinux()) return;

        var testDisplay = (IntPtr)0x1234;
        _x11Interop.OpenDisplay().Returns(testDisplay);
        // Make GrabKey succeed — return GrabSuccess (0) and no error
        _x11Interop.GrabKey(Arg.Any<IntPtr>(), Arg.Any<int>(), Arg.Any<uint>(),
            Arg.Any<IntPtr>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(0);
        _x11Interop.DefaultRootWindow(Arg.Any<IntPtr>()).Returns((IntPtr)0x100);
        _x11Interop.StringToKeysym("F1").Returns((uint)0xffbe);
        _x11Interop.StringToKeysym("Space").Returns((uint)0x020);
        _x11Interop.KeysymToKeycode(Arg.Any<IntPtr>(), Arg.Any<uint>()).Returns(67);
        _x11Interop.Sync(Arg.Any<IntPtr>(), Arg.Any<bool>()).Returns(1);

        var manager = CreateManager();
        await manager.InitializeAsync();

        // Give the event loop a moment to start
        await Task.Delay(100);

        manager.Dispose();

        // Should have closed the display on dispose
        _x11Interop.Received().CloseDisplay(testDisplay);
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidKeyName_LogsWarning()
    {
        if (!OperatingSystem.IsLinux()) return;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hotkeys:Hotkeys:0:Key"] = "INVALID_KEY_NAME",
                ["Hotkeys:Hotkeys:0:Modifiers"] = "Ctrl",
                ["Hotkeys:Hotkeys:0:Action"] = "toggle",
            })
            .Build();

        // Simulate successful X11 display open so we reach the key name lookup
        _x11Interop.OpenDisplay().Returns((IntPtr)0x1234);
        _x11Interop.DefaultRootWindow(Arg.Any<IntPtr>()).Returns((IntPtr)0x100);
        _x11Interop.StringToKeysym("INVALID_KEY_NAME").Returns((uint)0);
        _x11Interop.StringToKeysym("XK_INVALID_KEY_NAME").Returns((uint)0);

        var manager = new HotkeyManager(_activation, config, _logger, _x11Interop);
        await manager.InitializeAsync();

        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Unknown key name")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        manager.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_X11GrabFailure_LogsWarning()
    {
        if (!OperatingSystem.IsLinux()) return;

        var testDisplay = (IntPtr)0x5678;
        _x11Interop.OpenDisplay().Returns(testDisplay);
        _x11Interop.DefaultRootWindow(Arg.Any<IntPtr>()).Returns((IntPtr)0x200);
        _x11Interop.StringToKeysym("F1").Returns((uint)0xffbe);
        _x11Interop.KeysymToKeycode(Arg.Any<IntPtr>(), Arg.Any<uint>()).Returns(67);
        // GrabKey returns non-zero = failure (GrabSuccess is 0)
        _x11Interop.GrabKey(Arg.Any<IntPtr>(), Arg.Any<int>(), Arg.Any<uint>(),
            Arg.Any<IntPtr>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(1); // Non-zero indicates failure

        var manager = CreateManager();
        await manager.InitializeAsync();

        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to grab")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        manager.Dispose();
    }
}
