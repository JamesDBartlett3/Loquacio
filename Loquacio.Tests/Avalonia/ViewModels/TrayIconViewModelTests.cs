using Loquacio.Avalonia.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Loquacio.Tests.Avalonia.ViewModels;

public class TrayIconViewModelTests
{
    [Theory]
    [InlineData(true, true, "Listening", "#0000FF")]   // Blue - active
    [InlineData(false, false, "Disconnected", "#FF0000")] // Red - error
    [InlineData(false, true, "Connecting to engine", "#FFFF00")] // Yellow
    [InlineData(false, true, "Processing segment", "#FFFF00")]   // Yellow
    [InlineData(false, true, "Idle", "#808080")]         // Gray - idle
    public void ComputeStatusColor_MapsStatesCorrectly(bool listening, bool connected, string status, string expected)
    {
        var color = TrayIconViewModel.ComputeStatusColor(listening, connected, status);
        Assert.Equal(expected, color);
    }

    [Fact]
    public void UpdateState_SetsPropertiesAndColor()
    {
        var vm = new TrayIconViewModel(() => Task.CompletedTask, () => { }, () => { }, NullLogger<TrayIconViewModel>.Instance);
        vm.UpdateState(isListening: false, isConnected: false, statusText: "Lost connection");
        Assert.False(vm.IsListening);
        Assert.False(vm.IsConnected);
        Assert.Equal("Lost connection", vm.StatusText);
        Assert.Equal("#FF0000", vm.StatusColor);
    }

    [Fact]
    public void Commands_ExecuteCallbacks()
    {
        var toggled = false; var shown = false; var quit = false;
        var vm = new TrayIconViewModel(
            () => { toggled = true; return Task.CompletedTask; },
            () => shown = true,
            () => quit = true,
            NullLogger<TrayIconViewModel>.Instance);

        vm.ShowWindowCommand.Execute(null);
        vm.QuitCommand.Execute(null);
        vm.ToggleListeningCommand.Execute(null);

        Assert.True(toggled);
        Assert.True(shown);
        Assert.True(quit);
    }

    [Fact]
    public void ToString_ReflectsStatus()
    {
        var vm = new TrayIconViewModel(() => Task.CompletedTask, () => { }, () => { }, NullLogger<TrayIconViewModel>.Instance);
        vm.UpdateState(false, true, "Idle");
        Assert.Equal("Loquacio - Idle", vm.ToString());
    }
}
