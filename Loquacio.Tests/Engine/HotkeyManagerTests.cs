using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Loquacio.Engine.Interop;
using Loquacio.Engine.Models;
using Loquacio.Engine.Services;

namespace Loquacio.Tests.Engine;

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

    // ── Win32 seam tests (run on any OS; user32.dll is never touched) ──

    /// <summary>
    /// Fake Win32 interop: RegisterHotKey succeeds, GetMessage returns one
    /// WM_HOTKEY then WM_QUIT so the pump exits cleanly.
    /// </summary>
    private sealed class FakeWin32Interop : IWin32HotkeyInterop
    {
        public List<(IntPtr hwnd, int id, uint mods, uint vk)> Registered { get; } = [];
        public List<int> Unregistered { get; } = [];
        public IntPtr Window { get; set; } = (IntPtr)0x42;
        public bool WindowCreated { get; private set; }
        public bool WindowDestroyed { get; private set; }
        public bool RegisterSucceeds { get; set; } = true;
        public bool Destroyed { get; private set; }
        public Func<uint>? ThreadId { get; set; }
        private MSG? _pendingHotkey;

        public IntPtr GetModuleHandle(string? moduleName) => (IntPtr)0x10;

        public IntPtr CreateMessageOnlyWindow(string className)
        {
            WindowCreated = true;
            return Window;
        }

        public bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey)
        {
            if (!RegisterSucceeds) return false;
            Registered.Add((hwnd, id, modifiers, virtualKey));
            return true;
        }

        public bool UnregisterHotKey(IntPtr hwnd, int id)
        {
            Unregistered.Add(id);
            return true;
        }

        public bool DestroyWindow(IntPtr hwnd) { WindowDestroyed = true; return true; }

        public int GetMessage(out MSG msg, IntPtr hwnd, uint filterMin, uint filterMax)
        {
            if (_pendingHotkey is not null)
            {
                msg = _pendingHotkey.Value;
                _pendingHotkey = null;
                return 1;
            }
            // WM_QUIT → pump exits
            msg = default;
            return 0;
        }

        public bool TranslateMessage(ref MSG msg) => false;
        public IntPtr DispatchMessage(ref MSG msg) => IntPtr.Zero;

        public bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == 0x0012) Destroyed = true; // WM_QUIT requested
            return true;
        }

        public uint GetCurrentThreadId() => ThreadId?.Invoke() ?? 1234u;

        public void QueueHotkey(int id) => _pendingHotkey = new MSG
        {
            hWnd = Window,
            message = 0x0312, // WM_HOTKEY
            wParam = (IntPtr)id,
        };
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }
        return condition();
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(25);
        }
        return await condition();
    }

    private static HotkeyManager CreateWin32Manager(
        IActivationManagerService activation,
        FakeWin32Interop win32,
        IConfiguration? config = null) =>
        new(activation, config ?? new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<HotkeyManager>>(), x11Interop: null, win32Interop: win32,
            forcePlatform: "windows");

    [Fact]
    public async Task Win32_RegistersConfiguredHotkeys_ViaSeam()
    {
        var win32 = new FakeWin32Interop();
        var manager = CreateWin32Manager(_activation, win32);

        await manager.InitializeAsync();

        // Registration happens on the pump thread — wait for it.
        Assert.True(await WaitForAsync(() => win32.Registered.Count == 2, TimeSpan.FromSeconds(5)),
            "Expected 2 hotkey registrations");

        // Default hotkeys: Ctrl+Alt+F1 and Ctrl+Shift+Space → 2 registrations
        Assert.Equal(2, win32.Registered.Count);
        Assert.True(win32.WindowCreated);
        manager.Dispose();

        // All registrations must be released on dispose
        Assert.Equal(win32.Registered.Select(r => r.id), win32.Unregistered);
        Assert.True(win32.WindowDestroyed);
    }

    [Fact]
    public async Task Win32_RegisterHotKeyConflict_SkipsAndDoesNotThrow()
    {
        var win32 = new FakeWin32Interop { RegisterSucceeds = false };
        var manager = CreateWin32Manager(_activation, win32);

        await manager.InitializeAsync();

        Assert.Empty(win32.Registered);
        manager.Dispose();
        Assert.Empty(win32.Unregistered);
    }

    [Fact]
    public async Task Win32_WmHotkeyMessage_TogglesListening()
    {
        var win32 = new FakeWin32Interop();
        var activation = Substitute.For<IActivationManagerService>();
        win32.QueueHotkey(id: 0); // first registered hotkey → "toggle"
        var manager = CreateWin32Manager(activation, win32);

        await manager.InitializeAsync();
        // Wait for the pump thread to deliver the queued WM_HOTKEY
        Assert.True(await WaitForAsync(async () =>
            (await Task.Run(() => activation.ReceivedCalls()))
                .Any(c => c.GetMethodInfo().Name == "ToggleListeningAsync"), TimeSpan.FromSeconds(5)),
            "Expected ToggleListeningAsync to be dispatched");

        await activation.Received(1).ToggleListeningAsync();
        manager.Dispose();
    }

    [Fact]
    public async Task Win32_Dispose_PostsWmQuit_AndCleansUp()
    {
        var win32 = new FakeWin32Interop();
        var manager = CreateWin32Manager(_activation, win32);

        await manager.InitializeAsync();
        Assert.True(await WaitForAsync(() => win32.Registered.Count == 2, TimeSpan.FromSeconds(5)));
        manager.Dispose();

        Assert.True(await WaitForAsync(() => win32.Destroyed, TimeSpan.FromSeconds(5))); // WM_QUIT posted
        Assert.Equal(win32.Registered.Select(r => r.id), win32.Unregistered);
        Assert.True(win32.WindowDestroyed);
    }

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


    // ----- Settings-driven hotkey registration (Phase 5.x) -----

    private sealed class InMemorySettingsService : ISettingsService
    {
        public Loquacio.Models.Settings Settings { get; set; } = new();
        public event EventHandler? SettingsChanged;
        public Task<Loquacio.Models.Settings> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);
        public Task SaveSettingsAsync(Loquacio.Models.Settings settings, CancellationToken ct = default)
        {
            Settings = settings;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
        public void SimulateExternalChange() => SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static HotkeyManager CreateWin32SettingsManager(
        IActivationManagerService activation,
        FakeWin32Interop win32,
        InMemorySettingsService settings)
    {
        var config = new ConfigurationBuilder().Build();
        return new(activation, config, Substitute.For<ILogger<HotkeyManager>>(),
            x11Interop: null, win32Interop: win32, settingsService: settings,
            forcePlatform: "windows");
    }

    [Fact]
    public async Task SettingsHotkeys_AreRegisteredOnInitialize()
    {
        var win32 = new FakeWin32Interop();
        var settings = new InMemorySettingsService();
        using var manager = CreateWin32SettingsManager(_activation, win32, settings);

        await manager.InitializeAsync();

        // 2 config defaults + 4 activation hotkeys from Settings defaults,
        // registered asynchronously on the pump thread
        Assert.True(await WaitForAsync(
            () => Task.FromResult(win32.Registered.Count == 6),
            TimeSpan.FromSeconds(5)), "Expected 6 registrations");
    }

    [Fact]
    public async Task SettingsHotkey_EmptyBinding_IsSkipped()
    {
        var win32 = new FakeWin32Interop();
        var settings = new InMemorySettingsService();
        settings.Settings.Activation.StopHotkey = "";
        settings.Settings.Activation.CopyLastOutputHotkey = "";
        using var manager = CreateWin32SettingsManager(_activation, win32, settings);

        await manager.InitializeAsync();

        Assert.True(await WaitForAsync(
            () => Task.FromResult(win32.Registered.Count == 4),
            TimeSpan.FromSeconds(5)), "Expected 4 registrations"); // toggle + mode
    }

    [Fact]
    public async Task SettingsChanged_ReregistersHotkeys_WithNewCombo()
    {
        var win32 = new FakeWin32Interop();
        var settings = new InMemorySettingsService();
        using var manager = CreateWin32SettingsManager(_activation, win32, settings);
        await manager.InitializeAsync();
        Assert.True(await WaitForAsync(
            () => Task.FromResult(win32.Registered.Count == 6),
            TimeSpan.FromSeconds(5)), "Expected 6 initial registrations");
        var initialCount = win32.Registered.Count;

        settings.Settings.Activation.Hotkey = "Ctrl+Shift+Z";
        settings.SimulateExternalChange();

        // Old bindings torn down and re-registered with the new combo.
        // FakeWin32Interop.Registered is append-only: after one reapply there
        // are 12 registration records (6 old + 6 new), and the new set
        // includes VK 0x5A (Z key).
        Assert.True(await WaitForAsync(
            () => Task.FromResult(win32.Unregistered.Count >= 6),
            TimeSpan.FromSeconds(5)), "Expected old bindings unregistered");
        Assert.True(await WaitForAsync(
            () => Task.FromResult(win32.Registered.Count == initialCount * 2),
            TimeSpan.FromSeconds(5)), "Expected re-registration");
        Assert.Contains(win32.Registered, r => r.vk == 0x5A);
    }

    [Fact]
    public async Task ReapplyHotkeysAsync_AfterSettingsChange_DropsRemovedBinding()
    {
        var win32 = new FakeWin32Interop();
        var settings = new InMemorySettingsService();
        using var manager = CreateWin32SettingsManager(_activation, win32, settings);
        await manager.InitializeAsync();
        Assert.True(await WaitForAsync(
            () => Task.FromResult(win32.Registered.Count == 6),
            TimeSpan.FromSeconds(5)), "Expected 6 initial registrations");
        var before = win32.Registered.Count;

        settings.Settings.Activation.CopyLastOutputHotkey = "";
        await manager.ReapplyHotkeysAsync();

        // All 6 old bindings torn down; 5 re-registered (6 old + 5 new records)
        Assert.True(await WaitForAsync(
            () => Task.FromResult(win32.Unregistered.Count == before),
            TimeSpan.FromSeconds(5)), "Expected all old bindings unregistered");
        Assert.True(await WaitForAsync(
            () => Task.FromResult(win32.Registered.Count == before + before - 1),
            TimeSpan.FromSeconds(5)), "Expected 5 re-registered bindings");
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
