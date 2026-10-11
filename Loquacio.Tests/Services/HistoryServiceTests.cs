using Loquacio.Models;
using Loquacio.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Loquacio.Tests.Services;

public class HistoryServiceTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "loq-history-tests-" + Guid.NewGuid().ToString("N"));
    private HistoryService _svc = null!;

    public Task InitializeAsync()
    {
        _svc = new HistoryService(NullLogger<HistoryService>.Instance, _dir);
        return _svc.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _svc.DisposeAsync();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static HistoryEntry Entry(string text = "hello") => new()
    {
        RawText = text,
        CorrectedText = "",
        Model = "tiny",
        Language = "en",
    };

    [Fact]
    public async Task AddEntry_PersistsAcrossRestart()
    {
        await _svc.AddEntryAsync(Entry("first"));

        await using var svc2 = new HistoryService(NullLogger<HistoryService>.Instance, _dir);
        await svc2.InitializeAsync();
        var history = await svc2.GetHistoryAsync();
        Assert.Single(history);
        Assert.Equal("first", history[0].RawText);
    }

    [Fact]
    public async Task AddEntry_NewestFirst()
    {
        await _svc.AddEntryAsync(Entry("a"));
        await _svc.AddEntryAsync(Entry("b"));
        var history = await _svc.GetHistoryAsync();
        Assert.Equal("b", history[0].RawText);
        Assert.Equal("a", history[1].RawText);
    }

    [Fact]
    public async Task Rotation_TrimsTo1000Entries()
    {
        for (var i = 0; i < 1005; i++)
            await _svc.AddEntryAsync(Entry("e" + i));

        Assert.Equal(1000, await _svc.GetCountAsync());
        var history = await _svc.GetHistoryAsync();
        Assert.Equal("e1004", history[0].RawText);
        Assert.Equal("e5", history[999].RawText);
    }

    [Fact]
    public async Task GetRecentHistory_LimitsCount()
    {
        for (var i = 0; i < 5; i++)
            await _svc.AddEntryAsync(Entry("e" + i));

        var recent = await _svc.GetRecentHistoryAsync(2);
        Assert.Equal(2, recent.Count);
        Assert.Equal("e4", recent[0].RawText);
    }

    [Fact]
    public async Task DeleteEntry_RemovesOnlyThatEntry()
    {
        var e = Entry("target");
        await _svc.AddEntryAsync(e);
        await _svc.AddEntryAsync(Entry("other"));

        await _svc.DeleteEntryAsync(e.Id);

        var history = await _svc.GetHistoryAsync();
        Assert.Single(history);
        Assert.Equal("other", history[0].RawText);
    }

    [Fact]
    public async Task ClearHistory_EmptiesAndPersists()
    {
        await _svc.AddEntryAsync(Entry());
        await _svc.ClearHistoryAsync();

        Assert.Equal(0, await _svc.GetCountAsync());

        await using var svc2 = new HistoryService(NullLogger<HistoryService>.Instance, _dir);
        await svc2.InitializeAsync();
        Assert.Equal(0, await svc2.GetCountAsync());
    }

    [Fact]
    public void DisplayText_PrefersCorrectedText()
    {
        var e = Entry();
        Assert.Equal("hello", e.DisplayText);
        e.CorrectedText = "Hello!";
        Assert.Equal("Hello!", e.DisplayText);
    }
}
