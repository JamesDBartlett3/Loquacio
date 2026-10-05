using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Loquacio.Avalonia.Services;
using Loquacio.Avalonia.ViewModels;
using Loquacio.Ipc;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.Avalonia.Tests;

public class ControllerViewModelTests : IDisposable
{
    private readonly IDaemonProxy _daemon = Substitute.For<IDaemonProxy>();
    private readonly IDispatcherService _dispatcher = Substitute.For<IDispatcherService>();
    private readonly SettingsPersistenceService _settings;
    private readonly ILogger<ControllerViewModel> _logger = Substitute.For<ILogger<ControllerViewModel>>();

    private readonly ControllerViewModel _vm;
    private readonly string _tempSettingsPath;

    private readonly AckMessage _ack = new();

    public ControllerViewModelTests()
    {
        _tempSettingsPath = Path.GetTempFileName();
        _settings = new SettingsPersistenceService(
            Substitute.For<ILogger<SettingsPersistenceService>>(), _tempSettingsPath);

        _daemon.IsConnected.Returns(true);
        _daemon.SendCommandAsync(Arg.Any<IpcMessage>())
            .ReturnsForAnyArgs(Task.FromResult(_ack));

        _vm = new ControllerViewModel(
            _daemon, _dispatcher,
            new GeneralSettingsViewModel(),
            new AudioSettingsViewModel(),
            new WhisperSettingsViewModel(),
            new LLMSettingsViewModel(),
            new VocabularySettingsViewModel(),
            _settings, _logger);
    }

    public void Dispose()
    {
        _vm.Dispose();
        if (File.Exists(_tempSettingsPath)) File.Delete(_tempSettingsPath);
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_DoesNotDoubleConnect()
    {
        // First call should connect
        await _vm.InitializeAsync();
        await _daemon.Received(1).ConnectAsync();

        // Second call should be a no-op
        await _vm.InitializeAsync();
        await _daemon.Received(1).ConnectAsync(); // Still exactly 1, not 2
    }

    [Fact]
    public void InitialState_IsNotListening()
    {
        Assert.False(_vm.IsListening);
        Assert.False(_vm.IsConnected);
    }

    [Fact]
    public async Task ToggleListening_WhenIdle_SendsStartMessage()
    {
        _vm.IsListening = false;
        await _vm.ToggleListeningCommand.ExecuteAsync(null);
        await _daemon.Received(1).SendCommandAsync(Arg.Any<StartListeningMessage>());
    }

    [Fact]
    public async Task ToggleListening_WhenListening_SendsStopMessage()
    {
        _vm.IsListening = true;
        await _vm.ToggleListeningCommand.ExecuteAsync(null);
        await _daemon.Received(1).SendCommandAsync(Arg.Any<StopListeningMessage>());
    }

    [Fact]
    public async Task ToggleListening_WhenNotConnected_StillSendsCommand()
    {
        _daemon.IsConnected.Returns(false);
        _vm.IsListening = false;
        await _vm.ToggleListeningCommand.ExecuteAsync(null);
        await _daemon.Received(1).SendCommandAsync(Arg.Any<StartListeningMessage>());
    }

    [Fact]
    public void SaveAndLoadSettings_PersistsAllSettings()
    {
        // Set values
        _vm.General.ActivationMode = "push-to-talk";
        _vm.General.HotkeyDisplay = "Ctrl+Shift+K";
        _vm.General.Keyword = "Hey Computer";
        _vm.Audio.Gain = 2.0;
        _vm.Audio.SilenceThresholdMs = 2000;
        _vm.Whisper.ModelSize = "medium";
        _vm.Whisper.Language = "en";
        _vm.LLM.Enabled = false;
        _vm.LLM.Provider = "ollama";
        _vm.Vocabulary.CustomWords.Add("OpenClaw");

        _vm.SaveSettings();

        // Create a new VM and load
        var newSettings = new SettingsPersistenceService(
            Substitute.For<ILogger<SettingsPersistenceService>>(), _tempSettingsPath);
        var newVm = new ControllerViewModel(
            _daemon, _dispatcher,
            new GeneralSettingsViewModel(),
            new AudioSettingsViewModel(),
            new WhisperSettingsViewModel(),
            new LLMSettingsViewModel(),
            new VocabularySettingsViewModel(),
            newSettings, _logger);

        newVm.LoadSettings();

        Assert.Equal("push-to-talk", newVm.General.ActivationMode);
        Assert.Equal("Ctrl+Shift+K", newVm.General.HotkeyDisplay);
        Assert.Equal("Hey Computer", newVm.General.Keyword);
        Assert.Equal(2.0, newVm.Audio.Gain);
        Assert.Equal(2000, newVm.Audio.SilenceThresholdMs);
        Assert.Equal("medium", newVm.Whisper.ModelSize);
        Assert.Equal("en", newVm.Whisper.Language);
        Assert.False(newVm.LLM.Enabled);
        Assert.Equal("ollama", newVm.LLM.Provider);
        Assert.Contains("OpenClaw", newVm.Vocabulary.CustomWords);

        newVm.Dispose();
    }

    [Fact]
    public void TranscriptionHistory_HoldsInsertedItems()
    {
        _vm.TranscriptionHistory.Insert(0, new TranscriptionResult
        {
            Text = "Hello world", Timestamp = DateTime.Now
        });
        Assert.Single(_vm.TranscriptionHistory);
        Assert.Equal("Hello world", _vm.TranscriptionHistory[0].Text);
    }
}

public class GeneralSettingsViewModelTests
{
    [Fact]
    public void Defaults_Correct()
    {
        var vm = new GeneralSettingsViewModel();
        Assert.Equal("continuous", vm.ActivationMode);
        Assert.Equal("Ctrl+Alt+D", vm.HotkeyDisplay);
        Assert.True(vm.KeywordEnabled);
        Assert.Equal("Hey Dictate", vm.Keyword);
    }

    [Fact]
    public void SetHotkey_UpdatesDisplay()
    {
        var vm = new GeneralSettingsViewModel();
        vm.StartRecordingHotkeyCommand.Execute(null);
        Assert.True(vm.IsRecordingHotkey);
        vm.SetHotkey("Ctrl+Shift+K");
        Assert.Equal("Ctrl+Shift+K", vm.HotkeyDisplay);
        Assert.False(vm.IsRecordingHotkey);
    }

    [Fact]
    public void SetHotkey_IgnoresEmpty()
    {
        var vm = new GeneralSettingsViewModel();
        vm.HotkeyDisplay = "Ctrl+Alt+D";
        vm.SetHotkey("");
        Assert.Equal("Ctrl+Alt+D", vm.HotkeyDisplay);
    }

    [Fact]
    public void CancelHotkeyRecording_ExitsCaptureMode()
    {
        var vm = new GeneralSettingsViewModel();
        vm.StartRecordingHotkeyCommand.Execute(null);
        vm.CancelHotkeyRecordingCommand.Execute(null);
        Assert.False(vm.IsRecordingHotkey);
    }
}

public class AudioSettingsViewModelTests
{
    [Fact]
    public void Defaults_Correct()
    {
        var vm = new AudioSettingsViewModel();
        Assert.Equal("default", vm.SelectedDeviceId);
        Assert.Equal(1.0, vm.Gain);
        Assert.Equal(1500, vm.SilenceThresholdMs);
    }

    [Fact]
    public void VuMeterFill_ClampsTo01()
    {
        var vm = new AudioSettingsViewModel();
        vm.CurrentLevel = -0.5;
        Assert.Equal(0, vm.VuMeterFill);
        vm.CurrentLevel = 1.5;
        Assert.Equal(1, vm.VuMeterFill);
        vm.CurrentLevel = 0.5;
        Assert.Equal(0.5, vm.VuMeterFill);
    }
}

public class WhisperSettingsViewModelTests
{
    [Fact]
    public void Defaults_Correct()
    {
        var vm = new WhisperSettingsViewModel();
        Assert.Equal("base", vm.ModelSize);
        Assert.Equal("auto", vm.Language);
    }

    [Fact]
    public void AvailableModels_ContainsExpected()
    {
        var vm = new WhisperSettingsViewModel();
        Assert.Contains("tiny", vm.AvailableModels);
        Assert.Contains("base", vm.AvailableModels);
        Assert.Contains("small", vm.AvailableModels);
        Assert.Contains("medium", vm.AvailableModels);
        Assert.Contains("large-v3", vm.AvailableModels);
    }
}

public class LLMSettingsViewModelTests
{
    [Fact]
    public void Defaults_Correct()
    {
        var vm = new LLMSettingsViewModel();
        Assert.Equal("lm-studio", vm.Provider);
        Assert.Equal("http://localhost:1234/v1", vm.Endpoint);
        Assert.True(vm.Enabled);
    }
}

public class VocabularySettingsViewModelTests
{
    [Fact]
    public void AddWord_AddsAndClears()
    {
        var vm = new VocabularySettingsViewModel();
        vm.NewWord = "OpenClaw";
        vm.AddWordCommand.Execute(null);
        Assert.Empty(vm.NewWord);
        Assert.Contains("OpenClaw", vm.CustomWords);
    }

    [Fact]
    public void AddWord_SkipsDuplicates()
    {
        var vm = new VocabularySettingsViewModel();
        vm.NewWord = "Test";
        vm.AddWordCommand.Execute(null);
        vm.NewWord = "Test";
        vm.AddWordCommand.Execute(null);
        Assert.Single(vm.CustomWords);
    }

    [Fact]
    public void AddWord_SkipsEmpty()
    {
        var vm = new VocabularySettingsViewModel();
        vm.NewWord = "  ";
        vm.AddWordCommand.Execute(null);
        Assert.Empty(vm.CustomWords);
    }

    [Fact]
    public void RemoveWord_Removes()
    {
        var vm = new VocabularySettingsViewModel();
        vm.NewWord = "Test";
        vm.AddWordCommand.Execute(null);
        vm.RemoveWordCommand.Execute("Test");
        Assert.Empty(vm.CustomWords);
    }
}

public class SettingsPersistenceServiceTests : IDisposable
{
    private readonly string _tempPath = Path.GetTempFileName();

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public void SaveAndLoad_Roundtrips()
    {
        var svc = new SettingsPersistenceService(
            Substitute.For<ILogger<SettingsPersistenceService>>(), _tempPath);

        var settings = new AvaloniaSettings
        {
            ModelSize = "large-v3",
            Language = "en",
            Gain = 2.5,
            CustomWords = ["OpenClaw", "Kanban"]
        };

        svc.Save(settings);
        var loaded = svc.Load();

        Assert.NotNull(loaded);
        Assert.Equal("large-v3", loaded.ModelSize);
        Assert.Equal("en", loaded.Language);
        Assert.Equal(2.5, loaded.Gain);
        Assert.Equal(new List<string> { "OpenClaw", "Kanban" }, loaded.CustomWords);
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        var svc = new SettingsPersistenceService(
            Substitute.For<ILogger<SettingsPersistenceService>>(), "/nonexistent/path.json");
        Assert.Null(svc.Load());
    }
}
