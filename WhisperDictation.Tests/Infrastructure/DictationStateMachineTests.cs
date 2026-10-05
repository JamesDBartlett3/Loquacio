using WhisperDictation.Infrastructure;

namespace WhisperDictation.Tests.Infrastructure;

/// <summary>
/// Tests for the DictationStateMachine — declarative FSM for dictation runs.
/// </summary>
public class DictationStateMachineTests
{
    private static readonly DictationState[] AllStates =
    {
        DictationState.Idle,
        DictationState.Recording,
        DictationState.Transcribing,
        DictationState.PostProcessing,
        DictationState.Saving
    };

    private static readonly (DictationState From, DictationState To)[] LegalTransitions =
    {
        (DictationState.Idle, DictationState.Recording),
        (DictationState.Recording, DictationState.Transcribing),
        (DictationState.Transcribing, DictationState.PostProcessing),
        (DictationState.PostProcessing, DictationState.Saving),
        (DictationState.Saving, DictationState.Idle),
    };

    private static bool IsLegal(DictationState from, DictationState to) =>
        Array.Exists(LegalTransitions, t => t.From == from && t.To == to);

    /// <summary>Start a session and drive the FSM to the given state.</summary>
    private static (DictationStateMachine Fsm, Guid SessionId) ReachState(DictationState target)
    {
        var fsm = new DictationStateMachine();
        var sessionId = fsm.StartSession();

        if (target == DictationState.Idle)
            return (fsm, sessionId); // session started, still Idle

        DictationState[] pipeline =
        {
            DictationState.Recording,
            DictationState.Transcribing,
            DictationState.PostProcessing,
            DictationState.Saving
        };

        foreach (var step in pipeline.TakeWhile(s => s != target))
            fsm.TransitionTo(sessionId, step);
        fsm.TransitionTo(sessionId, target);

        Assert.Equal(target, fsm.State);
        return (fsm, sessionId);
    }

    [Fact]
    public void InitialState_IsIdle_WithNoSession()
    {
        var fsm = new DictationStateMachine();

        Assert.Equal(DictationState.Idle, fsm.State);
        Assert.Null(fsm.SessionId);
    }

    [Fact]
    public void AllLegalTransitions_Succeed_AndSetState()
    {
        foreach (var (from, to) in LegalTransitions)
        {
            var (fsm, sessionId) = ReachState(from);
            fsm.TransitionTo(sessionId, to);

            Assert.Equal(to, fsm.State);
        }
    }

    [Fact]
    public void AllIllegalTransitions_Throw()
    {
        var illegal = AllStates
            .SelectMany(from => AllStates.Select(to => (from, to)))
            .Where(pair => !IsLegal(pair.from, pair.to))
            // (Idle, Idle) is a defined no-op, not a rejection
            .Where(pair => !(pair.from == DictationState.Idle && pair.to == DictationState.Idle))
            .ToList();

        Assert.NotEmpty(illegal);
        foreach (var (from, to) in illegal)
        {
            var (fsm, sessionId) = ReachState(from);
            var ex = Assert.Throws<InvalidOperationException>(() => fsm.TransitionTo(sessionId, to));
            Assert.Contains(from.ToString(), ex.Message);
            Assert.Contains(to.ToString(), ex.Message);
            Assert.Equal(from, fsm.State); // state unchanged after rejection
        }
    }

    [Fact]
    public void StateChanged_Fires_ForEveryLegalTransition()
    {
        var fsm = new DictationStateMachine();
        var sessionId = fsm.StartSession();
        var events = new List<DictationStateChangedEventArgs>();
        fsm.StateChanged += e => events.Add(e);

        foreach (var (_, to) in LegalTransitions)
            fsm.TransitionTo(sessionId, to);

        Assert.Equal(5, events.Count);
        Assert.All(events, e => Assert.Equal(sessionId, e.SessionId));
        Assert.Equal(DictationState.Idle, events[^1].To);
    }

    [Theory]
    [InlineData(DictationState.Idle)]
    [InlineData(DictationState.Recording)]
    [InlineData(DictationState.Transcribing)]
    [InlineData(DictationState.PostProcessing)]
    [InlineData(DictationState.Saving)]
    public void Cancel_FromAnyState_ReturnsToIdle(DictationState from)
    {
        DictationStateMachine fsm;
        Guid sessionId = Guid.Empty;
        if (from == DictationState.Idle) {
            fsm = new DictationStateMachine();
        } else {
            (fsm, sessionId) = ReachState(from);
        }

        if (from == DictationState.Idle)
            fsm.Cancel(Guid.NewGuid()); // no-op even without an active run
        else
            fsm.Cancel(sessionId);

        Assert.Equal(DictationState.Idle, fsm.State);
        Assert.Null(fsm.SessionId);
    }

    [Fact]
    public void Cancel_FromIdle_IsNoOp()
    {
        var fsm = new DictationStateMachine();
        var anyId = Guid.NewGuid();
        var fired = false;
        fsm.StateChanged += _ => fired = true;

        fsm.Cancel(anyId);

        Assert.Equal(DictationState.Idle, fsm.State);
        Assert.False(fired);
    }

    [Fact]
    public void Cancel_FiresEventWithCancelledReason()
    {
        var (fsm, sessionId) = ReachState(DictationState.Recording);
        DictationStateChangedEventArgs? args = null;
        fsm.StateChanged += e => args = e;

        fsm.Cancel(sessionId);

        Assert.NotNull(args);
        Assert.Equal(DictationTransitionReason.Cancelled, args!.Reason);
        Assert.Equal(DictationState.Recording, args.From);
        Assert.Equal(DictationState.Idle, args.To);
    }

    [Fact]
    public void StaleSessionId_Transition_Rejected()
    {
        var (fsm, sessionId) = ReachState(DictationState.Recording);
        var staleId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(
            () => fsm.TransitionTo(staleId, DictationState.Transcribing));
        Assert.Equal(DictationState.Recording, fsm.State);
        Assert.Equal(sessionId, fsm.SessionId);
    }

    [Fact]
    public void StaleSessionId_Cancel_Rejected()
    {
        var (fsm, sessionId) = ReachState(DictationState.Recording);
        var staleId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => fsm.Cancel(staleId));
        Assert.Equal(DictationState.Recording, fsm.State);
        Assert.Equal(sessionId, fsm.SessionId);
    }

    [Fact]
    public void StartSession_WhileActive_Throws()
    {
        var (fsm, _) = ReachState(DictationState.Recording);

        Assert.Throws<InvalidOperationException>(() => fsm.StartSession());
    }

    [Fact]
    public void NewSession_AfterComplete_GetsFreshId()
    {
        var (fsm, firstId) = ReachState(DictationState.Saving);
        fsm.TransitionTo(firstId, DictationState.Idle);

        var secondId = fsm.StartSession();

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(secondId, fsm.SessionId);
    }

    [Fact]
    public async Task StageTimeout_Expiry_ForcesCancelToIdle()
    {
        var fsm = new DictationStateMachine();
        var sessionId = fsm.StartSession();
        DictationStateChangedEventArgs? args = null;
        fsm.StateChanged += e => args = e;

        var stageToken = fsm.TransitionTo(
            sessionId, DictationState.Recording, stageTimeout: TimeSpan.FromMilliseconds(50));

        await Task.Delay(300);

        Assert.True(stageToken.IsCancellationRequested);
        Assert.Equal(DictationState.Idle, fsm.State);
        Assert.Null(fsm.SessionId);
        Assert.NotNull(args);
        Assert.Equal(DictationTransitionReason.Timeout, args!.Reason);
        Assert.Equal(DictationState.Recording, args.From);
    }

    [Fact]
    public async Task StageTimeout_LegalTransitionBeforeExpiry_ArmingIsCleared()
    {
        var fsm = new DictationStateMachine();
        var sessionId = fsm.StartSession();

        fsm.TransitionTo(sessionId, DictationState.Recording, stageTimeout: TimeSpan.FromMilliseconds(100));
        await Task.Delay(50);
        fsm.TransitionTo(sessionId, DictationState.Transcribing); // clears Recording's timer
        await Task.Delay(200);

        Assert.Equal(DictationState.Transcribing, fsm.State);
    }

    [Fact]
    public void StageTimeout_WhileIdle_IsNoOp()
    {
        var fsm = new DictationStateMachine();
        var sessionId = fsm.StartSession();

        var token = fsm.TransitionTo(sessionId, DictationState.Idle, stageTimeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(DictationState.Idle, fsm.State);
        Assert.Equal(CancellationToken.None, token);
    }

    [Fact]
    public void Cancel_ImmediatelyCancelsStageToken()
    {
        var fsm = new DictationStateMachine();
        var sessionId = fsm.StartSession();
        var stageToken = fsm.TransitionTo(
            sessionId, DictationState.Recording, stageTimeout: TimeSpan.FromSeconds(30));

        fsm.Cancel(sessionId);

        Assert.True(stageToken.IsCancellationRequested);
        Assert.Equal(DictationState.Idle, fsm.State);
    }

    [Fact]
    public void LegalTransition_CancelsPreviousStageToken()
    {
        var fsm = new DictationStateMachine();
        var sessionId = fsm.StartSession();
        var recordingToken = fsm.TransitionTo(
            sessionId, DictationState.Recording, stageTimeout: TimeSpan.FromSeconds(30));

        var transcribingToken = fsm.TransitionTo(sessionId, DictationState.Transcribing);

        Assert.True(recordingToken.IsCancellationRequested); // prior stage work must not linger
        Assert.False(transcribingToken.IsCancellationRequested);
        Assert.Equal(DictationState.Transcribing, fsm.State);
    }

    [Fact]
    public async Task CancelledRun_TimeoutDoesNotFireLate()
    {
        var fsm = new DictationStateMachine();
        var sessionId = fsm.StartSession();
        fsm.TransitionTo(sessionId, DictationState.Recording, stageTimeout: TimeSpan.FromMilliseconds(100));

        fsm.Cancel(sessionId);
        await Task.Delay(300);

        Assert.Equal(DictationState.Idle, fsm.State);
        Assert.Null(fsm.SessionId);
    }
}
