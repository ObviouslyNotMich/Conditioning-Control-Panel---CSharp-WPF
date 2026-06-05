using System;

namespace ConditioningControlPanel.Services;

public sealed class NativeOverlayHookSessionMachine
{
    private readonly uint _selfPid = (uint)Environment.ProcessId;
    private uint? _targetPid;

    public NativeHookSessionState State { get; private set; } = NativeHookSessionState.Uninitialized;
    public NativeOverlayTargetSnapshot? CurrentTarget { get; private set; }

    public event Action<NativeHookSessionState, NativeHookSessionState, string>? StateChanged;

    public void Start(bool nativeReady)
    {
        if (!nativeReady)
        {
            TransitionTo(NativeHookSessionState.Fallback, "probe-not-ready");
            return;
        }

        TransitionTo(NativeHookSessionState.Idle, "host-start");
    }

    public void Stop()
    {
        _targetPid = null;
        CurrentTarget = null;
        TransitionTo(NativeHookSessionState.Stopped, "host-stop");
    }

    public void OnTargetChanged(NativeOverlayTargetSnapshot? target)
    {
        CurrentTarget = target;

        if (State == NativeHookSessionState.Fallback || State == NativeHookSessionState.Stopped)
            return;

        if (!target.HasValue)
        {
            _targetPid = null;
            TransitionTo(NativeHookSessionState.Idle, "no-foreground-target");
            return;
        }

        var snapshot = target.Value;
        _targetPid = snapshot.ProcessId;

        // Never attach to ourselves.
        if (snapshot.ProcessId == _selfPid)
        {
            TransitionTo(NativeHookSessionState.TrackingTarget, "self-foreground-skip");
            return;
        }

        TransitionTo(NativeHookSessionState.TrackingTarget, "target-detected");

        if (!snapshot.IsAttachReady)
        {
            TransitionTo(NativeHookSessionState.Faulted, "attach-query-denied");
            return;
        }

        TransitionTo(NativeHookSessionState.AttachPending, "attach-begin");
    }

    public void MarkAttachSucceeded(string reason)
    {
        if (State == NativeHookSessionState.Stopped || State == NativeHookSessionState.Fallback)
            return;

        TransitionTo(NativeHookSessionState.Attached, reason);
    }

    public void MarkAttachFailed(string reason, bool fallback)
    {
        if (fallback)
        {
            TransitionTo(NativeHookSessionState.Fallback, reason);
            return;
        }

        TransitionTo(NativeHookSessionState.Faulted, reason);
    }

    public void MarkDetached(string reason)
    {
        if (State == NativeHookSessionState.Stopped || State == NativeHookSessionState.Fallback)
            return;

        _targetPid = null;
        TransitionTo(NativeHookSessionState.Idle, reason);
    }

    public void TriggerFallback(string reason)
    {
        TransitionTo(NativeHookSessionState.Fallback, reason);
    }

    public void OnProbeFault(string reason)
    {
        TransitionTo(NativeHookSessionState.Faulted, reason);
    }

    private void TransitionTo(NativeHookSessionState newState, string reason)
    {
        if (State == newState) return;
        var old = State;
        State = newState;
        StateChanged?.Invoke(old, newState, reason);
    }
}
