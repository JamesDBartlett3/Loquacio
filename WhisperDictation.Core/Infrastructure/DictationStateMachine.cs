using System.Collections.Generic;
using System;

namespace WhisperDictation.Infrastructure;

/// <summary>States of the dictation pipeline FSM.</summary>
public enum DictationState
{
    Idle,
    Recording,
    Transcribing,
    PostProcessing,
    Saving
}

/// <summary>Why a state change happened.</summary>
public enum DictationTransitionReason
{
    Normal,
    Cancelled,
    Timeout
}

/// <summary>Details of a completed state change.</summary>
public sealed record DictationStateChangedEventArgs(
    Guid SessionId,
    DictationState From,
    DictationState To,
    DictationTransitionReason Reason);

/// <summary>
/// Explicit, declarative state machine for a dictation run.
/// Each run owns a session id; commands carrying a stale/unknown session id
/// are rejected. Each active stage accepts a timeout that forces a cancel
/// back to Idle when it expires. Thread-safe.
/// </summary>
public sealed class DictationStateMachine
{
    private static readonly Dictionary<(DictationState From, DictationState To), bool> LegalTransitions = new()
    {
        [(DictationState.Idle, DictationState.Recording)] = true,
        [(DictationState.Recording, DictationState.Transcribing)] = true,
        [(DictationState.Transcribing, DictationState.PostProcessing)] = true,
        [(DictationState.PostProcessing, DictationState.Saving)] = true,
        [(DictationState.Saving, DictationState.Idle)] = true,
    };

    private readonly object _lock = new();
    private Guid? _sessionId;
    private DictationState _state = DictationState.Idle;
    private CancellationTokenSource? _stageTimeoutCts;

    /// <summary>Raised after every completed state change.</summary>
    public event Action<DictationStateChangedEventArgs>? StateChanged;

    public DictationState State { get { lock (_lock) return _state; } }

    /// <summary>Session id of the current/last run, or null if never started.</summary>
    public Guid? SessionId { get { lock (_lock) return _sessionId; } }

    /// <summary>
    /// Start a new dictation run from Idle and return its session id.
    /// Throws if a run is already active.
    /// </summary>
    public Guid StartSession()
    {
        lock (_lock)
        {
            if (_state != DictationState.Idle)
                throw new InvalidOperationException(
                    $"Cannot start a session while state is {_state}. Cancel or complete the active run first.");

            _sessionId = Guid.NewGuid();
            return _sessionId.Value;
        }
    }

    /// <summary>
    /// Advance the active session to <paramref name="target"/>.
    /// <paramref name="stageTimeout"/>, when provided, arms a safety timeout:
    /// if it expires while still in <paramref name="target"/>, the machine
    /// cancels itself back to Idle (reason = Timeout). The returned token is
    /// cancelled on timeout/cancel and should be passed to the stage's work.
    /// Throws InvalidOperationException for illegal transitions or a stale
    /// session id.
    /// </summary>
    public CancellationToken TransitionTo(Guid sessionId, DictationState target, TimeSpan? stageTimeout = null)
    {
        lock (_lock)
        {
            if (_state == DictationState.Idle && target == DictationState.Idle)
                return CancellationToken.None; // no-op, regardless of session

            EnsureCurrentSession(sessionId);
            if (!LegalTransitions.TryGetValue((_state, target), out var legal) || !legal)
                throw new InvalidOperationException(
                    $"Illegal transition {_state} -> {target} for session {sessionId}.");

            DisposeStageTimeout();

            var previous = _state;
            _state = target;

            if (target == DictationState.Idle)
            {
                _sessionId = null;
            }
            else if (stageTimeout is { } timeout)
            {
                _stageTimeoutCts = new CancellationTokenSource(timeout);
                var timedOutCts = _stageTimeoutCts;
                _stageTimeoutCts.Token.Register(() => ForceCancelFromTimeout(timedOutCts));
            }

            RaiseChanged(new DictationStateChangedEventArgs(
                sessionId, previous, target, DictationTransitionReason.Normal));

            return _stageTimeoutCts?.Token ?? CancellationToken.None;
        }
    }

    /// <summary>
    /// Cancel the active session from any state and return to Idle.
    /// No-op when already Idle. Throws for a stale session id.
    /// </summary>
    public void Cancel(Guid sessionId)
    {
        lock (_lock)
        {
            if (_state == DictationState.Idle)
                return; // idempotent no-op

            EnsureCurrentSession(sessionId);

            var previous = _state;
            DisposeStageTimeout();
            _state = DictationState.Idle;
            _sessionId = null;

            RaiseChanged(new DictationStateChangedEventArgs(
                sessionId, previous, DictationState.Idle, DictationTransitionReason.Cancelled));
        }
    }

    private void ForceCancelFromTimeout(CancellationTokenSource expectedCts)
    {
        lock (_lock)
        {
            if (_state == DictationState.Idle || !object.ReferenceEquals(_stageTimeoutCts, expectedCts))
                return; // stale timer, already moved on

            var sessionId = _sessionId!.Value;
            var previous = _state;
            DisposeStageTimeout();
            _state = DictationState.Idle;
            _sessionId = null;

            RaiseChanged(new DictationStateChangedEventArgs(
                sessionId, previous, DictationState.Idle, DictationTransitionReason.Timeout));
        }
    }

    private void EnsureCurrentSession(Guid sessionId)
    {
        if (_sessionId != sessionId)
            throw new InvalidOperationException(
                $"Session {sessionId} is stale or unknown; active session is {_sessionId?.ToString() ?? "<none>"}.");
    }

    private void DisposeStageTimeout()
    {
        // Clear the field BEFORE cancelling: the timeout registration
        // (ForceCancelFromTimeout) re-enters on Cancel and must see a stale
        // reference so a normal transition's dispose doesn't masquerade as a
        // timeout. Cancelling (not just disposing) honours the documented
        // contract that the stage token is cancelled on timeout/cancel, so
        // in-flight stage work actually aborts.
        if (_stageTimeoutCts is { } cts)
        {
            _stageTimeoutCts = null;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
            cts.Dispose();
        }
    }

    private void RaiseChanged(DictationStateChangedEventArgs args)
    {
        StateChanged?.Invoke(args);
    }
}
