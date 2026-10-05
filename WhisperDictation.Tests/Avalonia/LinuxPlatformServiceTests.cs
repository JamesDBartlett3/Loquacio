using Microsoft.Extensions.Logging;
using NSubstitute;
using WhisperDictation.Avalonia.Services;

namespace WhisperDictation.Avalonia.Tests;

public class LinuxPlatformServiceTests : IDisposable
{
    private readonly ILogger<LinuxPlatformService> _logger = Substitute.For<ILogger<LinuxPlatformService>>();
    private readonly LinuxPlatformService _service;
    private readonly string _testConfigHome;

    public LinuxPlatformServiceTests()
    {
        // Use isolated XDG_CONFIG_HOME for testing
        _testConfigHome = Path.Combine(Path.GetTempPath(), $"xdg-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _testConfigHome);

        _service = new LinuxPlatformService(_logger);
    }

    public void Dispose()
    {
        _service.DisableAutostart();
        if (Directory.Exists(_testConfigHome))
            Directory.Delete(_testConfigHome, true);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
    }

    [Fact]
    public void IsLinux_ReturnsCorrectPlatform()
    {
        // On Linux test runner this is true, on Windows false
        Assert.Equal(OperatingSystem.IsLinux(), _service.IsLinux);
    }

    [Fact]
    public void CurrentDesktop_ReturnsEnvironmentValue()
    {
        var expected = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        Assert.Equal(expected, _service.CurrentDesktop);
    }

    [Fact]
    public void EnableAutostart_CreatesDesktopEntry()
    {
        // Skip on non-Linux
        if (!OperatingSystem.IsLinux()) return;

        _service.EnableAutostart("/usr/local/bin/whisper-dictation-avalonia");

        Assert.True(_service.IsAutostartEnabled());

        var desktopFile = Path.Combine(_testConfigHome, "autostart", "whisper-dictation.desktop");
        Assert.True(File.Exists(desktopFile));

        var content = File.ReadAllText(desktopFile);
        Assert.Contains("[Desktop Entry]", content);
        Assert.Contains("Type=Application", content);
        Assert.Contains("Exec=/usr/local/bin/whisper-dictation-avalonia", content);
        Assert.Contains("Name=Whisper Dictation", content);
    }

    [Fact]
    public void DisableAutostart_RemovesDesktopEntry()
    {
        if (!OperatingSystem.IsLinux()) return;

        _service.EnableAutostart("/usr/local/bin/test-app");
        Assert.True(_service.IsAutostartEnabled());

        _service.DisableAutostart();
        Assert.False(_service.IsAutostartEnabled());
    }

    [Fact]
    public void IsAutostartEnabled_FalseWhenNoEntry()
    {
        Assert.False(_service.IsAutostartEnabled());
    }

    [Fact]
    public void EnableAutostart_CustomDisplayName_AppearsInFile()
    {
        if (!OperatingSystem.IsLinux()) return;

        _service.EnableAutostart("/usr/bin/test", "My Custom Dictation");

        var desktopFile = Path.Combine(_testConfigHome, "autostart", "whisper-dictation.desktop");
        var content = File.ReadAllText(desktopFile);
        Assert.Contains("Name=My Custom Dictation", content);
    }

    [Fact]
    public void EnableAutostart_Twice_OverwritesExisting()
    {
        if (!OperatingSystem.IsLinux()) return;

        _service.EnableAutostart("/usr/bin/first", "First");
        _service.EnableAutostart("/usr/bin/second", "Second");

        var desktopFile = Path.Combine(_testConfigHome, "autostart", "whisper-dictation.desktop");
        var content = File.ReadAllText(desktopFile);

        Assert.Contains("Exec=/usr/bin/second", content);
        Assert.DoesNotContain("Exec=/usr/bin/first", content);
    }

    [Fact]
    public void GetDataDirectory_FollowsXDGSpec()
    {
        var dataDir = _service.GetDataDirectory();
        Assert.EndsWith("whisper-dictation", dataDir);
    }

    [Fact]
    public void GetConfigDirectory_FollowsXDGSpec()
    {
        var configDir = _service.GetConfigDirectory();
        Assert.EndsWith("whisper-dictation", configDir);
    }

    [Fact]
    public void DesktopEnvironment_DoesNotThrow()
    {
        // Just verify it doesn't throw and returns something (even null on non-Linux)
        var de = _service.DesktopEnvironment;
        // No assertion needed — just ensuring it doesn't throw
    }
}

public class NullLinuxPlatformServiceTests
{
    private readonly NullLinuxPlatformService _service = new();

    [Fact]
    public void IsLinux_False()
    {
        Assert.False(_service.IsLinux);
    }

    [Fact]
    public void DesktopEnvironment_Null()
    {
        Assert.Null(_service.DesktopEnvironment);
    }

    [Fact]
    public void CurrentDesktop_Null()
    {
        Assert.Null(_service.CurrentDesktop);
    }

    [Fact]
    public void EnableAutostart_NoOp()
    {
        _service.EnableAutostart("/usr/bin/test");
        Assert.False(_service.IsAutostartEnabled());
    }

    [Fact]
    public void DisableAutostart_NoOp()
    {
        var exception = Record.Exception(() => _service.DisableAutostart());
        Assert.Null(exception);
    }

    [Fact]
    public void IsAutostartEnabled_False()
    {
        Assert.False(_service.IsAutostartEnabled());
    }
}
