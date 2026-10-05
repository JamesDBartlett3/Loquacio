using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Loquacio.Daemon.Services;
using Loquacio.Services;

namespace Loquacio.Tests.Daemon;

/// <summary>
/// Regression tests for the clipboard fallback in DaemonClipboardService.TypeTextAsync:
/// when keystroke injection is blocked (UIPI / elevated window / focus loss),
/// the transcription MUST land on the clipboard instead of being dropped.
/// </summary>
public class DaemonClipboardServiceTests
{
    private sealed class RecordingClipboardService(ITextInjectionService injection)
        : DaemonClipboardService(injection, NullLogger<DaemonClipboardService>.Instance)
    {
        public List<(string cmd, string stdin)> Commands { get; } = [];

        protected override Task RunAsync(string cmd, string stdin, string args, CancellationToken ct)
        {
            Commands.Add((cmd, stdin));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TypeText_InjectionBlocked_FallsBackToClipboard()
    {
        var injection = Substitute.For<ITextInjectionService>();
        injection.InjectTextAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(false);
        var svc = new RecordingClipboardService(injection);

        await svc.TypeTextAsync("hello world");

        var fallback = Assert.Single(svc.Commands);
        Assert.Equal("hello world", fallback.stdin);
    }

    [Fact]
    public async Task TypeText_InjectionSucceeded_DoesNotTouchClipboard()
    {
        var injection = Substitute.For<ITextInjectionService>();
        injection.InjectTextAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
        var svc = new RecordingClipboardService(injection);

        await svc.TypeTextAsync("hello world");

        Assert.Empty(svc.Commands);
    }

    [Fact]
    public async Task TypeText_EmptyText_NoOps()
    {
        var injection = Substitute.For<ITextInjectionService>();
        var svc = new RecordingClipboardService(injection);

        await svc.TypeTextAsync("");

        Assert.Empty(svc.Commands);
        await injection.DidNotReceive().InjectTextAsync(Arg.Any<string>(), Arg.Any<bool>());
    }
}
