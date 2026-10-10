using Loquacio.Avalonia.ViewModels;
using Loquacio.Ipc;
using Loquacio.Models;
using Loquacio.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Loquacio.Tests.Avalonia.ViewModels;

public class HistoryViewModelTests
{
    private static (HistoryViewModel vm, IEngineProxy engine, IClipboardService clipboard) Create(List<HistoryEntry>? payload = null)
    {
        var engine = Substitute.For<IEngineProxy>();
        var json = System.Text.Json.JsonSerializer.Serialize(payload ?? []);
        engine.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
              .Returns(new AckMessage { Success = true, Error = json });
        var clipboard = Substitute.For<IClipboardService>();
        var vm = new HistoryViewModel(engine, clipboard, NullLogger<HistoryViewModel>.Instance);
        return (vm, engine, clipboard);
    }

    [Fact]
    public async Task Load_PopulatesEntriesNewestFirst()
    {
        var entries = new List<HistoryEntry>
        {
            new() { RawText = "newest", CorrectedText = "", Model = "tiny", Language = "en" },
            new() { RawText = "oldest", CorrectedText = "", Model = "tiny", Language = "en" },
        };
        var (vm, _, _) = Create(entries);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.EntryCount);
        Assert.Equal("newest", vm.Entries[0].RawText);
        Assert.Equal("2 entries", vm.EntryCountText);
    }

    [Fact]
    public async Task Load_FailedAck_LeavesEntriesEmpty()
    {
        var engine = Substitute.For<IEngineProxy>();
        engine.SendCommandAsync(Arg.Any<IpcMessage>(), Arg.Any<CancellationToken>())
              .Returns(new AckMessage { Success = false, Error = "boom" });
        var vm = new HistoryViewModel(engine, Substitute.For<IClipboardService>(), NullLogger<HistoryViewModel>.Instance);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Entries);
        Assert.Equal(0, vm.EntryCount);
    }

    [Fact]
    public async Task CopySelected_CopiesDisplayText()
    {
        var (vm, _, clipboard) = Create();
        vm.SelectedEntry = new HistoryEntry { RawText = "raw", CorrectedText = "corrected", Model = "tiny", Language = "en" };

        await vm.CopySelectedCommand.ExecuteAsync(null);

        await clipboard.Received(1).CopyToClipboardAsync("corrected", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearAll_SendsCommandAndEmpties()
    {
        var entries = new List<HistoryEntry> { new() { RawText = "x", CorrectedText = "", Model = "tiny", Language = "en" } };
        var (vm, engine, _) = Create(entries);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.EntryCount);

        await vm.ClearAllCommand.ExecuteAsync(null);

        Assert.Empty(vm.Entries);
        await engine.Received().SendCommandAsync(Arg.Is<ClearHistoryMessage>(_ => true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ParsePayload_Null_ReturnsNull()
    {
        Assert.Null(HistoryViewModel.ParseHistoryPayload(null));
    }
}
