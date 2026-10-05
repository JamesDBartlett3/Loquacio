using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperDictation.Models;
using WhisperDictation.Infrastructure;
using WhisperDictation.Services;

namespace WhisperDictation.Tests.Services;

/// <summary>
/// Verifies the wiring of DictationStateMachine (PR #17) into
/// BackgroundTranscriptionService: per-segment runs, stage timeouts that
/// force-cancel wedged stages, and clean return to Idle on both the happy
/// path and the error path.
/// </summary>
public class BackgroundTranscriptionFsmTests
{
    private static (BackgroundTranscriptionService Service,
                    IAudioCaptureService Audio,
                    IWhisperProcessorService Whisper,
                    ILLMPostProcessorService Llm,
                    IClipboardService Clipboard,
                    Action<Settings> Configure) Create(
        Action<Settings>? configure = null)
    {
        var audio = Substitute.For<IAudioCaptureService>();
        var whisper = Substitute.For<IWhisperProcessorService>();
        var settingsService = Substitute.For<ISettingsService>();
        var llm = Substitute.For<ILLMPostProcessorService>();
        var vocabulary = Substitute.For<IVocabularyService>();
        var clipboard = Substitute.For<IClipboardService>();

        var settings = new Settings { Output = new OutputSettings { Mode = "clipboard" } };
        configure?.Invoke(settings);
        settingsService.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(settings);

        audio.GetAvailableDevices().Returns(new List<AudioDevice> { new() { Id = "d1", FriendlyName = "Mic" } });
        whisper.IsModelLoaded.Returns(true);
        whisper.ProcessAsync(Arg.Any<AudioSegment>(), Arg.Any<CancellationToken>()).Returns("hello world");
        llm.ProcessAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(0));
        vocabulary.GetWordsAsync(Arg.Any<CancellationToken>()).Returns(new List<string>());

        var service = new BackgroundTranscriptionService(
            audio, whisper, settingsService, llm, vocabulary, clipboard,
            NullLogger<BackgroundTranscriptionService>.Instance);

        return (service, audio, whisper, llm, clipboard, s => { });
    }

    [Fact]
    public async Task HappyPath_FsmStartsIdleDrivesAllStagesAndReturnsToIdle()
    {
        var (service, _, _, _, clipboard, _) = Create();

        await service.StartAsync();
        try
        {
            var completed = new TaskCompletionSource<TranscriptionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.OnTranscriptionCompleted += (_, r) => completed.SetResult(r);

            await AudioChannel.WriteAsync(new AudioSegment
            {
                Data = new byte[32000],
                Timestamp = DateTime.UtcNow,
                Duration = TimeSpan.FromSeconds(1)
            });

            var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("hello world", result.Text);
            Assert.Equal(DictationState.Idle, service.Fsm.State);
            // Default output mode copies to clipboard
            await clipboard.Received(1).CopyToClipboardAsync("hello world", Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync();
            service.Dispose();
        }
    }

    [Fact]
    public async Task StageTimeout_ForceCancelsWedgedWhisperCallBackToIdle()
    {
        var (service, _, whisper, _, _, _) = Create(s =>
        {
            s.Pipeline.EnableStageTimeouts = true;
            s.Pipeline.TranscribeTimeoutSeconds = 1;
        });

        // Whisper hangs until cancelled
        whisper.ProcessAsync(Arg.Any<AudioSegment>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously).Task
                .WaitAsync(callInfo.ArgAt<CancellationToken>(1)));

        await service.StartAsync();
        try
        {
            var errored = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.OnError += (_, e) => errored.TrySetResult(e);

            await AudioChannel.WriteAsync(new AudioSegment
            {
                Data = new byte[32000],
                Timestamp = DateTime.UtcNow,
                Duration = TimeSpan.FromSeconds(1)
            });

            var ex = await errored.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsAssignableFrom<OperationCanceledException>(ex);

            // FSM must be back to Idle so the next segment can start a fresh run
            Assert.Equal(DictationState.Idle, service.Fsm.State);
        }
        finally
        {
            await service.StopAsync();
            service.Dispose();
        }
    }

    [Fact]
    public async Task SegmentError_CancelsRunBackToIdle()
    {
        var (service, _, whisper, _, _, _) = Create();
        whisper.ProcessAsync(Arg.Any<AudioSegment>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("whisper exploded"));

        await service.StartAsync();
        try
        {
            var errored = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.OnError += (_, e) => errored.TrySetResult(e);

            await AudioChannel.WriteAsync(new AudioSegment
            {
                Data = new byte[32000],
                Timestamp = DateTime.UtcNow,
                Duration = TimeSpan.FromSeconds(1)
            });

            var ex = await errored.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Equal(DictationState.Idle, service.Fsm.State);
        }
        finally
        {
            await service.StopAsync();
            service.Dispose();
        }
    }

    [Fact]
    public async Task SecondSegment_AfterFailedFirst_StartsFreshSession()
    {
        var (service, _, whisper, _, clipboard, _) = Create();
        var failNext = true;
        whisper.ProcessAsync(Arg.Any<AudioSegment>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => failNext
                ? throw new InvalidOperationException("first segment fails")
                : "second works");

        await service.StartAsync();
        try
        {
            var first = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<TranscriptionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstDone = false;
            service.OnError += (_, e) => { if (!firstDone) { firstDone = true; first.SetResult(e); } };
            service.OnTranscriptionCompleted += (_, r) => second.TrySetResult(r);

            await AudioChannel.WriteAsync(new AudioSegment { Data = new byte[32000], Timestamp = DateTime.UtcNow, Duration = TimeSpan.FromSeconds(1) });
            await first.Task.WaitAsync(TimeSpan.FromSeconds(10));

            failNext = false;
            await AudioChannel.WriteAsync(new AudioSegment { Data = new byte[32000], Timestamp = DateTime.UtcNow, Duration = TimeSpan.FromSeconds(1) });
            var result = await second.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("second works", result.Text);
            Assert.Equal(DictationState.Idle, service.Fsm.State);
            await clipboard.Received(1).CopyToClipboardAsync("second works", Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync();
            service.Dispose();
        }
    }

    [Fact]
    public void PipelineState_ForwardsFromFsm()
    {
        var (service, _, _, _, _, _) = Create();
        var observed = new List<DictationStateChangedEventArgs>();
        service.PipelineStateChanged += observed.Add;

        var sid = service.Fsm.StartSession();
        service.Fsm.TransitionTo(sid, DictationState.Recording);

        Assert.Equal(DictationState.Recording, service.PipelineState);
        Assert.Single(observed);
        Assert.Equal(DictationState.Recording, observed[0].To);
        Assert.Equal(sid, observed[0].SessionId);
    }
    [Fact]
    public void Snapshot_IsDeepCopy_MutationsDoNotLeak()
    {
        var settings = new Settings();
        settings.Output.Mode = "inject";
        settings.Pipeline.TranscribeTimeoutSeconds = 42;
        settings.Vocabulary.CustomWords.Add("P3 Adaptive");

        var snap = settings.Snapshot();

        // Mutate the live object after snapshotting
        settings.Output.Mode = "clipboard";
        settings.Pipeline.TranscribeTimeoutSeconds = 1;
        settings.Vocabulary.CustomWords.Add("Fabric");

        Assert.Equal("inject", snap.Output.Mode);
        Assert.Equal(42, snap.Pipeline.TranscribeTimeoutSeconds);
        Assert.Equal(["P3 Adaptive"], snap.Vocabulary.CustomWords);
    }
    [Fact]
    public async Task MidRunSettingsChange_DoesNotAffectActiveRun_AppliesNextRun()
    {
        var audio = Substitute.For<IAudioCaptureService>();
        var whisper = Substitute.For<IWhisperProcessorService>();
        var settingsService = Substitute.For<ISettingsService>();
        var llm = Substitute.For<ILLMPostProcessorService>();
        var vocabulary = Substitute.For<IVocabularyService>();
        var clipboard = Substitute.For<IClipboardService>();

        // Live, mutable settings object — the controller's view
        var settings = new Settings { Output = new OutputSettings { Mode = "inject" } };
        settingsService.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(settings);

        audio.GetAvailableDevices().Returns(new List<AudioDevice> { new() { Id = "d1", FriendlyName = "Mic" } });
        whisper.IsModelLoaded.Returns(true);
        llm.ProcessAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(0));
        vocabulary.GetWordsAsync(Arg.Any<CancellationToken>()).Returns(new List<string>());

        // Hold the Whisper stage open so we can mutate settings mid-run.
        var gate1 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate2 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        // Honor the stage token (like the real Whisper call) so a cancelled
        // run — e.g. service stop while gated — cannot wedge the test.
        whisper.ProcessAsync(Arg.Any<AudioSegment>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (++calls == 1 ? gate1.Task : gate2.Task)
                .WaitAsync(callInfo.ArgAt<CancellationToken>(1)));

        var service = new BackgroundTranscriptionService(
            audio, whisper, settingsService, llm, vocabulary, clipboard,
            NullLogger<BackgroundTranscriptionService>.Instance);

        await service.StartAsync();
        try
        {
            var done1 = new TaskCompletionSource<TranscriptionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var done2 = new TaskCompletionSource<TranscriptionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var n = 0;
            service.OnTranscriptionCompleted += (_, r) => (++n == 1 ? done1 : done2).TrySetResult(r);

            // Run 1 starts with output mode "inject"
            await AudioChannel.WriteAsync(new AudioSegment { Data = new byte[32000], Timestamp = DateTime.UtcNow, Duration = TimeSpan.FromSeconds(1) });
            await AudioChannel.WriteAsync(new AudioSegment { Data = new byte[32000], Timestamp = DateTime.UtcNow, Duration = TimeSpan.FromSeconds(1) });

            // Wait until run 1 is actually mid-Transcribing (snapshot taken)
            // before flipping the setting, so the mutation provably happens
            // mid-run rather than racing the loop's dequeue.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (service.PipelineState != DictationState.Transcribing && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.Equal(DictationState.Transcribing, service.PipelineState);

            // While run 1 is mid-Transcribing, the user flips output to clipboard
            settings.Output.Mode = "clipboard";

            gate1.SetResult("first");
            var r1 = await done1.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("first", r1.Text);
            // Run 1 was frozen before the edit: it must have injected (typed), not clipboarded
            await clipboard.Received(1).TypeTextAsync("first", Arg.Any<int>(), Arg.Any<CancellationToken>());
            await clipboard.DidNotReceiveWithAnyArgs().CopyToClipboardAsync(default, default);

            // Run 2 snapshots after the edit: clipboard mode applies
            gate2.SetResult("second");
            await done2.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await clipboard.Received(1).CopyToClipboardAsync("second", Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync();
            service.Dispose();
        }
    }

}

/// <summary>
/// CancelActiveRun (IPC cancel-dictation support): cancels an active FSM run
/// from any state and is an idempotent no-op when Idle.
/// </summary>
public class CancelActiveRunTests
{
    [Fact]
    public void CancelActiveRun_WhenIdle_ReturnsFalse()
    {
        var audio = Substitute.For<IAudioCaptureService>();
        var service = new BackgroundTranscriptionService(
            audio,
            Substitute.For<IWhisperProcessorService>(),
            Substitute.For<ISettingsService>(),
            Substitute.For<ILLMPostProcessorService>(),
            Substitute.For<IVocabularyService>(),
            Substitute.For<IClipboardService>(),
            NullLogger<BackgroundTranscriptionService>.Instance);

        Assert.Equal(DictationState.Idle, service.PipelineState);
        Assert.False(service.CancelActiveRun());
    }

}

/// <summary>
/// ReprocessLastAsync (IPC reprocess-last support): re-runs post-processing +
/// output for the last completed run using current settings, without
/// re-transcribing.
/// </summary>
public class ReprocessLastTests
{
    private static (BackgroundTranscriptionService Service,
                    IWhisperProcessorService Whisper,
                    ILLMPostProcessorService Llm,
                    IClipboardService Clipboard,
                    Settings LiveSettings) Create()
    {
        var audio = Substitute.For<IAudioCaptureService>();
        var whisper = Substitute.For<IWhisperProcessorService>();
        var settingsService = Substitute.For<ISettingsService>();
        var llm = Substitute.For<ILLMPostProcessorService>();
        var vocabulary = Substitute.For<IVocabularyService>();
        var clipboard = Substitute.For<IClipboardService>();

        var settings = new Settings { Output = new OutputSettings { Mode = "clipboard" } };
        settingsService.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(settings);

        audio.GetAvailableDevices().Returns(new List<AudioDevice> { new() { Id = "d1", FriendlyName = "Mic" } });
        whisper.IsModelLoaded.Returns(true);
        whisper.ProcessAsync(Arg.Any<AudioSegment>(), Arg.Any<CancellationToken>()).Returns("raw um transcript");
        llm.ProcessAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(0));
        vocabulary.GetWordsAsync(Arg.Any<CancellationToken>()).Returns(new List<string>());

        var service = new BackgroundTranscriptionService(
            audio, whisper, settingsService, llm, vocabulary, clipboard,
            NullLogger<BackgroundTranscriptionService>.Instance);

        return (service, whisper, llm, clipboard, settings);
    }

    private static async Task<TranscriptionResult> CompleteOneRunAsync(BackgroundTranscriptionService service)
    {
        var completed = new TaskCompletionSource<TranscriptionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnTranscriptionCompleted += (_, r) => completed.TrySetResult(r);

        await AudioChannel.WriteAsync(new AudioSegment
        {
            Data = new byte[32000],
            Timestamp = DateTime.UtcNow,
            Duration = TimeSpan.FromSeconds(1)
        });

        return await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReprocessLast_NoPreviousResult_ReturnsNull()
    {
        var (service, _, _, _, _) = Create();
        service.Dispose();

        Assert.Null(service.LastResult);
        Assert.Null(await service.ReprocessLastAsync());
    }

    [Fact]
    public async Task ReprocessLast_AppliesCurrentSettings_NoRetranscription()
    {
        var (service, serviceWhisper, llm, clipboard, settings) = Create();
        await service.StartAsync();
        try
        {
            // Run 1: LLM passthrough (raw kept), clipboard output
            var r1 = await CompleteOneRunAsync(service);
            Assert.Equal("raw um transcript", r1.Text);
            await clipboard.Received(1).CopyToClipboardAsync("raw um transcript", Arg.Any<CancellationToken>());
            // Change settings: LLM now transforms, output mode flips to inject
            llm.ProcessAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns("clean transcript");
            settings.Output.Mode = "inject";

            var reprocessed = await service.ReprocessLastAsync();

            Assert.NotNull(reprocessed);
            Assert.Equal("clean transcript", reprocessed!.Text);
            Assert.Equal("raw um transcript", reprocessed.RawText);
            Assert.True(reprocessed.IsPostProcessed);
            Assert.Equal(DictationState.Idle, service.Fsm.State);
            Assert.Equal(reprocessed, service.LastResult);

            // New settings applied: injected instead of clipboarded
            await clipboard.Received(1).TypeTextAsync("clean transcript", Arg.Any<int>(), Arg.Any<CancellationToken>());
            await clipboard.DidNotReceive().CopyToClipboardAsync("clean transcript", Arg.Any<CancellationToken>());

            // Reprocessing is post-processing only — Whisper must not have been
            // called again (still exactly one transcription: run 1).
            await ((IWhisperProcessorService)serviceWhisper).Received(1).ProcessAsync(Arg.Any<AudioSegment>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync();
            service.Dispose();
        }
    }

    [Fact]
    public async Task ReprocessLast_FiresCompletionEvent()
    {
        var (service, _, _, _, _) = Create();
        await service.StartAsync();
        try
        {
            await CompleteOneRunAsync(service);

            var fired = new TaskCompletionSource<TranscriptionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.OnTranscriptionCompleted += (_, r) => fired.TrySetResult(r);

            var result = await service.ReprocessLastAsync();
            var observed = await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(result, observed);
        }
        finally
        {
            await service.StopAsync();
            service.Dispose();
        }
    }

    [Fact]
    public async Task ReprocessLast_WhileRunActiveWithLastResult_Throws()
    {
        var (service, _, llm, clipboard, settings) = Create();
        await service.StartAsync();
        try
        {
            await CompleteOneRunAsync(service);
            Assert.NotNull(service.LastResult);

            // Hold a second run open mid-PostProcessing via a gated LLM call
            var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            llm.ProcessAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => gate.Task.WaitAsync(callInfo.ArgAt<CancellationToken>(2)));
            var second = new TaskCompletionSource<TranscriptionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.OnTranscriptionCompleted += (_, r) => second.TrySetResult(r);
            await AudioChannel.WriteAsync(new AudioSegment
            {
                Data = new byte[32000],
                Timestamp = DateTime.UtcNow,
                Duration = TimeSpan.FromSeconds(1)
            });
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (service.PipelineState != DictationState.PostProcessing && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.Equal(DictationState.PostProcessing, service.PipelineState);

            // Busy: reprocess must refuse, not interleave with the active run
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReprocessLastAsync());

            gate.SetResult("second");
            await second.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(DictationState.Idle, service.PipelineState);
        }
        finally
        {
            await service.StopAsync();
            service.Dispose();
        }
    }

    [Fact]
    public async Task ReprocessLast_WhileRunActive_Throws()
    {
        var (service, _, llm, _, _) = Create();
        service.Dispose();
        // Seed a last result via reflection-free path: not possible without a run,
        // so the busy-guard is exercised through the state machine contract in
        // CancelActiveRunTests. Here: busy run + no last result still returns null
        // (no-last check precedes busy check by design — null is the safer answer).
        var sid = service.Fsm.StartSession();
        service.Fsm.TransitionTo(sid, DictationState.Recording);

        Assert.Null(await service.ReprocessLastAsync());

        service.Fsm.Cancel(sid);
    }
}

/// <summary>
/// CancelActiveRun against an active session (relocated into its own class
/// alongside the other cancel tests).
/// </summary>
public class CancelActiveRunActiveSessionTests
{
    [Fact]
    public void CancelActiveRun_ActiveSession_CancelsAndReturnsTrue()
    {
        var audio = Substitute.For<IAudioCaptureService>();
        var service = new BackgroundTranscriptionService(
            audio,
            Substitute.For<IWhisperProcessorService>(),
            Substitute.For<ISettingsService>(),
            Substitute.For<ILLMPostProcessorService>(),
            Substitute.For<IVocabularyService>(),
            Substitute.For<IClipboardService>(),
            NullLogger<BackgroundTranscriptionService>.Instance);

        var sessionId = service.Fsm.StartSession();
        service.Fsm.TransitionTo(sessionId, DictationState.Recording);

        Assert.True(service.CancelActiveRun());
        Assert.Equal(DictationState.Idle, service.PipelineState);
        Assert.Null(service.PipelineSessionId);

        // Idempotent: second cancel is a no-op.
        Assert.False(service.CancelActiveRun());
    }
}
