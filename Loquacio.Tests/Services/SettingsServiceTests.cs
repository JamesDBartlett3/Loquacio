using NSubstitute;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.Tests.Services;

public class ISettingsServiceTests
{
    private readonly ISettingsService _mockSettings;

    public ISettingsServiceTests()
    {
        _mockSettings = Substitute.For<ISettingsService>();
    }

    [Fact]
    public async Task GetSettingsAsync_ReturnsDefaultSettings()
    {
        // Arrange
        var expectedSettings = new Settings();
        _mockSettings.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(expectedSettings);

        // Act
        var result = await _mockSettings.GetSettingsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("default", result.Audio.DeviceId);
        Assert.Equal("auto", result.Whisper.Language);
        Assert.Equal("lm-studio", result.LLM.Provider);
    }

    [Fact]
    public async Task GetSettingsAsync_ReturnsModifiedSettings()
    {
        // Arrange
        var settings = new Settings();
        settings.Audio.DeviceId = "usb-mic-001";
        settings.Whisper.Language = "en";
        settings.LLM.Provider = "ollama";
        settings.Vocabulary.CustomWords.Add("Power BI");

        _mockSettings.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(settings);

        // Act
        var result = await _mockSettings.GetSettingsAsync();

        // Assert
        Assert.Equal("usb-mic-001", result.Audio.DeviceId);
        Assert.Equal("en", result.Whisper.Language);
        Assert.Equal("ollama", result.LLM.Provider);
        Assert.Contains("Power BI", result.Vocabulary.CustomWords);
    }

    [Fact]
    public async Task SaveSettingsAsync_CalledWithSettings()
    {
        // Arrange
        var settings = new Settings();
        settings.Audio.Gain = 2.0;

        // Act
        await _mockSettings.SaveSettingsAsync(settings);

        // Assert
        await _mockSettings.Received(1).SaveSettingsAsync(settings);
    }

    [Fact]
    public void SettingsChanged_EventCanBeRaised()
    {
        // Arrange
        var eventRaised = false;
        _mockSettings.SettingsChanged += (sender, args) => eventRaised = true;

        // Act
        _mockSettings.SettingsChanged += Raise.Event<EventHandler>(_mockSettings, EventArgs.Empty);

        // Assert
        Assert.True(eventRaised);
    }
}
