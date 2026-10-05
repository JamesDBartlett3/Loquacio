using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperDictation.Models;
using WhisperDictation.Services;

namespace WhisperDictation.Tests.Services;

/// <summary>
/// Keyword activation is disabled (no working keyword recognizer exists).
/// These tests pin the disable behavior: the daemon never enters
/// KeywordActivated mode and never starts the keyword detector.
/// </summary>
public class ActivationManagerServiceTests
{
    private readonly IHotkeyService _hotkeyService = Substitute.For<IHotkeyService>();
    private readonly IKeywordDetectionService _keywordDetection = Substitute.For<IKeywordDetectionService>();
    private readonly IBackgroundTranscriptionService _transcription = Substitute.For<IBackgroundTranscriptionService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private ActivationManagerService CreateManager()
    {
        return new ActivationManagerService(
            _hotkeyService, _keywordDetection, _transcription, _settings,
            NullLogger<ActivationManagerService>.Instance);
    }

    private static Settings SettingsWithMode(string mode, bool keywordEnabled = true)
    {
        return new Settings
        {
            Activation = new ActivationSettings
            {
                Mode = mode,
                KeywordEnabled = keywordEnabled,
                Keyword = "Hey Dictate",
            }
        };
    }

    private void SetupSettings(Settings settings)
    {
        _settings.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(settings);
    }

    [Fact]
    public async Task InitializeAsync_KeywordModeRequested_FallsBackToContinuous()
    {
        SetupSettings(SettingsWithMode("keyword"));
        var manager = CreateManager();

        await manager.InitializeAsync();

        Assert.Equal(ActivationMode.Continuous, manager.CurrentMode);
        await _keywordDetection.DidNotReceive().StartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeAsync_ContinuousMode_DoesNotStartKeywordDetection()
    {
        SetupSettings(SettingsWithMode("continuous", keywordEnabled: true));
        var manager = CreateManager();

        await manager.InitializeAsync();

        Assert.Equal(ActivationMode.Continuous, manager.CurrentMode);
        await _keywordDetection.DidNotReceive().StartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchModeAsync_KeywordActivated_IsRefused()
    {
        SetupSettings(SettingsWithMode("continuous"));
        var manager = CreateManager();
        await manager.InitializeAsync();

        var modeChangedRaised = false;
        manager.ModeChanged += (_, _) => modeChangedRaised = true;

        await manager.SwitchModeAsync(ActivationMode.KeywordActivated);

        // Still in the original mode, no transition occurred
        Assert.Equal(ActivationMode.Continuous, manager.CurrentMode);
        Assert.False(modeChangedRaised);
        Assert.False(manager.IsListening);
    }

    [Fact]
    public async Task SwitchModeAsync_PushToTalk_StillWorks()
    {
        SetupSettings(SettingsWithMode("continuous"));
        var manager = CreateManager();
        await manager.InitializeAsync();

        await manager.SwitchModeAsync(ActivationMode.PushToTalk);

        Assert.Equal(ActivationMode.PushToTalk, manager.CurrentMode);
    }
}
