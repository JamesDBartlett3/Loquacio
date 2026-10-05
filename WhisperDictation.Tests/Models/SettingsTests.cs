using WhisperDictation.Models;

namespace WhisperDictation.Tests.Models;

public class SettingsTests
{
    [Fact]
    public void Settings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.NotNull(settings.Audio);
        Assert.NotNull(settings.Whisper);
        Assert.NotNull(settings.LLM);
        Assert.NotNull(settings.Activation);
        Assert.NotNull(settings.Vocabulary);
        Assert.NotNull(settings.Output);
        Assert.NotNull(settings.Tray);
    }

    [Fact]
    public void AudioSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var audio = new AudioSettings();

        // Assert
        Assert.Equal("default", audio.DeviceId);
        Assert.Equal(1.0, audio.Gain);
        Assert.Equal(1500, audio.SilenceThresholdMs);
    }

    [Fact]
    public void WhisperSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var whisper = new WhisperSettings();

        // Assert
        Assert.Equal(string.Empty, whisper.ModelPath);
        Assert.Equal("auto", whisper.Language);
    }

    [Fact]
    public void LLMSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var llm = new LLMSettings();

        // Assert
        Assert.Equal("lm-studio", llm.Provider);
        Assert.Equal("http://localhost:1234/v1", llm.Endpoint);
        Assert.Equal("auto", llm.Model);
    }

    [Fact]
    public void ActivationSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var activation = new ActivationSettings();

        // Assert
        Assert.Equal("continuous", activation.Mode);
        Assert.Equal("Ctrl+Alt+D", activation.Hotkey);
        Assert.True(activation.KeywordEnabled);
        Assert.Equal("Hey Dictate", activation.Keyword);
    }

    [Fact]
    public void VocabularySettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var vocab = new VocabularySettings();

        // Assert
        Assert.NotNull(vocab.CustomWords);
        Assert.Empty(vocab.CustomWords);
    }

    [Fact]
    public void OutputSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var output = new OutputSettings();

        // Assert — default is now "inject" (type into the focused window)
        Assert.Equal("inject", output.Mode);
        Assert.True(output.ToastEnabled);
    }

    [Fact]
    public void TraySettings_DefaultValues_AreCorrect()
    {
        var tray = new TraySettings();
        Assert.True(tray.MinimizeToTray);
        Assert.True(tray.CloseToTray);
        Assert.False(tray.StartMinimized);
        Assert.False(tray.AutoStart);
        Assert.True(tray.NotificationsEnabled);
    }

    [Fact]
    public void TraySettings_CanBeModified()
    {
        var tray = new TraySettings
        {
            MinimizeToTray = false,
            CloseToTray = false,
            StartMinimized = true,
            AutoStart = true,
            NotificationsEnabled = false
        };
        Assert.False(tray.MinimizeToTray);
        Assert.False(tray.CloseToTray);
        Assert.True(tray.StartMinimized);
        Assert.True(tray.AutoStart);
        Assert.False(tray.NotificationsEnabled);
    }

    [Fact]
    public void Settings_CanBeModified()
    {
        // Arrange
        var settings = new Settings();

        // Act
        settings.Audio.DeviceId = "usb-mic-001";
        settings.Audio.Gain = 2.5;
        settings.Whisper.ModelPath = @"C:\models\ggml-base.bin";
        settings.Whisper.Language = "en";
        settings.LLM.Provider = "ollama";
        settings.LLM.Endpoint = "http://localhost:11434/v1";
        settings.Activation.Mode = "push-to-talk";
        settings.Vocabulary.CustomWords.Add("Power BI");
        settings.Vocabulary.CustomWords.Add("DAX");
        settings.Output.Mode = "type";

        // Assert
        Assert.Equal("usb-mic-001", settings.Audio.DeviceId);
        Assert.Equal(2.5, settings.Audio.Gain);
        Assert.Equal(@"C:\models\ggml-base.bin", settings.Whisper.ModelPath);
        Assert.Equal("en", settings.Whisper.Language);
        Assert.Equal("ollama", settings.LLM.Provider);
        Assert.Equal("http://localhost:11434/v1", settings.LLM.Endpoint);
        Assert.Equal("push-to-talk", settings.Activation.Mode);
        Assert.Contains("Power BI", settings.Vocabulary.CustomWords);
        Assert.Contains("DAX", settings.Vocabulary.CustomWords);
        Assert.Equal("type", settings.Output.Mode);
    }
}
