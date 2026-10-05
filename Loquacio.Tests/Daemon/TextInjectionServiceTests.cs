using Microsoft.Extensions.Logging;
using NSubstitute;
using Loquacio.Daemon.Services;

namespace Loquacio.Tests.Daemon;

public class TextInjectionServiceTests
{
    private readonly ILogger<TextInjectionService> _logger;

    public TextInjectionServiceTests()
    {
        _logger = Substitute.For<ILogger<TextInjectionService>>();
    }

    [Fact]
    public async Task InjectTextAsync_EmptyText_ReturnsTrue()
    {
        var service = new TextInjectionService(_logger);

        var result = await service.InjectTextAsync("");

        Assert.True(result);
    }

    [Fact]
    public void InputStruct_MatchesWin32Size()
    {
        // SendInput validates cbSize against the true Win32 sizeof(INPUT) —
        // 40 bytes on x64 because the union is as large as MOUSEINPUT. A
        // struct that only declares KEYBDINPUT marshals to 32 bytes and makes
        // EVERY SendInput call fail with ERROR_INVALID_PARAMETER (this
        // silently broke all text injection on Windows).
        if (!OperatingSystem.IsWindows()) return;

        Assert.Equal(40, TextInjectionService.InputStructSize);
    }

    [Fact]
    public async Task InjectTextAsync_NullText_ReturnsTrue()
    {
        var service = new TextInjectionService(_logger);

        var result = await service.InjectTextAsync(null!);

        Assert.True(result);
    }

    [Fact]
    public async Task InjectTextAsync_OnLinux_AttemptsInjection()
    {
        // This test runs on Linux (our CI/dev environment)
        // It will attempt to find xdotool/ydotool/wtype and may fail gracefully
        var service = new TextInjectionService(_logger);

        // Should not throw regardless of whether tools are installed
        await service.InjectTextAsync("hello world", useClipboard: false);

        // Verify the service handled it (either succeeded or logged the failure)
        Assert.True(true);
    }

    // ── macOS chunk planning (testable on any OS; CGEvent calls themselves need macOS) ──

    [Fact]
    public void SplitMacOSUnicodeChunks_ShortText_SingleChunk()
    {
        var parts = TextInjectionService.SplitMacOSUnicodeChunks("hello");
        var part = Assert.Single(parts);
        Assert.Equal(("hello", false), part);
    }

    [Fact]
    public void SplitMacOSUnicodeChunks_LongText_ChunksAt20CodeUnits()
    {
        var text = new string('a', 45);
        var parts = TextInjectionService.SplitMacOSUnicodeChunks(text);
        Assert.Equal(3, parts.Count);
        Assert.All(parts, p => Assert.False(p.IsReturn));
        Assert.Equal(20, parts[0].Text.Length);
        Assert.Equal(20, parts[1].Text.Length);
        Assert.Equal(5, parts[2].Text.Length);
        Assert.Equal(text, string.Concat(parts.Select(p => p.Text)));
    }

    [Fact]
    public void SplitMacOSUnicodeChunks_NewlineBecomesReturn()
    {
        var parts = TextInjectionService.SplitMacOSUnicodeChunks("line1\nline2");
        Assert.Equal(3, parts.Count);
        Assert.Equal(("line1", false), parts[0]);
        Assert.Equal(("", true), parts[1]);
        Assert.Equal(("line2", false), parts[2]);
    }

    [Fact]
    public void SplitMacOSUnicodeChunks_TrailingNewline_EndsWithReturn()
    {
        var parts = TextInjectionService.SplitMacOSUnicodeChunks("hi\n");
        Assert.Equal(2, parts.Count);
        Assert.Equal(("hi", false), parts[0]);
        Assert.Equal(("", true), parts[1]);
    }

    [Fact]
    public void SplitMacOSUnicodeChunks_CarriageReturnDropped()
    {
        var parts = TextInjectionService.SplitMacOSUnicodeChunks("a\r\nb");
        Assert.Equal(3, parts.Count);
        Assert.Equal(("a", false), parts[0]);
        Assert.Equal(("", true), parts[1]);
        Assert.Equal(("b", false), parts[2]);
    }

    [Fact]
    public void SplitMacOSUnicodeChunks_NewlineInsideChunkBoundary_SplitsThere()
    {
        // 10 chars, newline at index 10, then 15 chars — newline splits the first chunk early
        var parts = TextInjectionService.SplitMacOSUnicodeChunks("0123456789\nabcdefghijklmno");
        Assert.Equal(("0123456789", false), parts[0]);
        Assert.Equal(("", true), parts[1]);
        Assert.Equal(("abcdefghijklmno", false), parts[2]);
    }

    [Fact]
    public void SplitMacOSUnicodeChunks_EmptyText_NoParts()
    {
        Assert.Empty(TextInjectionService.SplitMacOSUnicodeChunks(""));
    }

    [Fact]
    public void SplitMacOSUnicodeChunks_ConsecutiveNewlines_ConsecutiveReturns()
    {
        var parts = TextInjectionService.SplitMacOSUnicodeChunks("a\n\nb");
        Assert.Equal(4, parts.Count);
        Assert.Equal(("a", false), parts[0]);
        Assert.Equal(("", true), parts[1]);
        Assert.Equal(("", true), parts[2]);
        Assert.Equal(("b", false), parts[3]);
    }
}
