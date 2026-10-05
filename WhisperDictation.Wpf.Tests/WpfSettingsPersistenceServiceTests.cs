using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WhisperDictation.Wpf.Tests;

public class WpfSettingsPersistenceServiceTests
{
    private static string GetTempFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wpf-test-{Guid.NewGuid():N}.json");
        return path;
    }

    [Fact]
    public void Load_NoFile_ReturnsNull()
    {
        var logger = Substitute.For<ILogger<WpfSettingsPersistenceService>>();
        var svc = new WpfSettingsPersistenceService(logger, GetTempFilePath());

        var result = svc.Load();

        Assert.Null(result);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllProperties()
    {
        var path = GetTempFilePath();
        try
        {
            var logger = Substitute.For<ILogger<WpfSettingsPersistenceService>>();
            var svc = new WpfSettingsPersistenceService(logger, path);

            var settings = new WpfControllerSettings
            {
                ActivationMode = "push-to-talk",
                HotkeyDisplay = "Ctrl+Shift+V",
                KeywordEnabled = false,
                Keyword = "Computer",
                OutputMode = "type-out",
                NotificationsEnabled = false,
                SelectedDeviceId = "device-42",
                Gain = 2.5,
                SilenceThresholdMs = 2000,
                ModelSize = "medium",
                Language = "en",
                LlmEnabled = false,
                LlmProvider = "ollama",
                LlmEndpoint = "http://localhost:11434/v1",
                LlmModel = "llama3",
                CustomWords = ["alpha", "beta", "gamma"],
                WindowWidth = 1024,
                WindowHeight = 768,
                MinimizeToTray = false,
                CloseToTray = false,
                StartWithWindows = true,
                AutoStartDaemon = false,
                InProcessDaemon = true,
            };

            svc.Save(settings);
            var loaded = svc.Load();

            Assert.NotNull(loaded);
            Assert.Equal("push-to-talk", loaded!.ActivationMode);
            Assert.Equal("Ctrl+Shift+V", loaded.HotkeyDisplay);
            Assert.False(loaded.KeywordEnabled);
            Assert.Equal("Computer", loaded.Keyword);
            Assert.Equal("type-out", loaded.OutputMode);
            Assert.False(loaded.NotificationsEnabled);
            Assert.Equal("device-42", loaded.SelectedDeviceId);
            Assert.Equal(2.5, loaded.Gain);
            Assert.Equal(2000, loaded.SilenceThresholdMs);
            Assert.Equal("medium", loaded.ModelSize);
            Assert.Equal("en", loaded.Language);
            Assert.False(loaded.LlmEnabled);
            Assert.Equal("ollama", loaded.LlmProvider);
            Assert.Equal("http://localhost:11434/v1", loaded.LlmEndpoint);
            Assert.Equal("llama3", loaded.LlmModel);
            Assert.Equal(["alpha", "beta", "gamma"], loaded.CustomWords);
            Assert.Equal(1024, loaded.WindowWidth);
            Assert.Equal(768, loaded.WindowHeight);
            Assert.False(loaded.MinimizeToTray);
            Assert.False(loaded.CloseToTray);
            Assert.True(loaded.StartWithWindows);
            Assert.False(loaded.AutoStartDaemon);
            Assert.True(loaded.InProcessDaemon);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wpf-test-dir-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "subfolder", "settings.json");
        try
        {
            var logger = Substitute.For<ILogger<WpfSettingsPersistenceService>>();
            var svc = new WpfSettingsPersistenceService(logger, path);

            svc.Save(new WpfControllerSettings());

            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_CorruptJson_ReturnsNull()
    {
        var path = GetTempFilePath();
        try
        {
            File.WriteAllText(path, "{ this is not valid json }");
            var logger = Substitute.For<ILogger<WpfSettingsPersistenceService>>();
            var svc = new WpfSettingsPersistenceService(logger, path);

            var result = svc.Load();

            Assert.Null(result);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var settings = new WpfControllerSettings();

        Assert.Equal("continuous", settings.ActivationMode);
        Assert.Equal("Ctrl+Alt+D", settings.HotkeyDisplay);
        Assert.True(settings.KeywordEnabled);
        Assert.Equal("Hey Dictate", settings.Keyword);
        Assert.Equal("clipboard", settings.OutputMode);
        Assert.True(settings.NotificationsEnabled);
        Assert.Equal("default", settings.SelectedDeviceId);
        Assert.Equal(1.0, settings.Gain);
        Assert.Equal(1500, settings.SilenceThresholdMs);
        Assert.Equal("base", settings.ModelSize);
        Assert.Equal("auto", settings.Language);
        Assert.True(settings.LlmEnabled);
        Assert.Equal("lm-studio", settings.LlmProvider);
        Assert.Equal("http://localhost:1234/v1", settings.LlmEndpoint);
        Assert.True(settings.MinimizeToTray);
        Assert.True(settings.CloseToTray);
        Assert.True(settings.AutoStartDaemon);
        Assert.False(settings.InProcessDaemon);
    }
}
