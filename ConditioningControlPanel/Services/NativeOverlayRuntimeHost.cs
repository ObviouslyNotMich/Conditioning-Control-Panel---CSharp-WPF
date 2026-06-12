using System;
using System.Threading;

namespace ConditioningControlPanel.Services;

public sealed class NativeOverlayRuntimeHost : IDisposable
{
    private readonly NativeOverlayProbeResult _probe;
    private readonly NativeOverlayTargetProcessTracker _tracker;
    private readonly NativeOverlayHookSessionMachine _session;
    private readonly NativeOverlayD3DRendererBridge _renderer;
    private readonly NativeOverlayCaptureSessionBridge _capture;
    private bool _isStarted;
    private bool _disposed;
    private NativeOverlayDesiredState _desiredState;
    private Timer? _frameTimer;

    public NativeOverlayRuntimeHost(NativeOverlayProbeResult probe)
    {
        _probe = probe;
        _tracker = new NativeOverlayTargetProcessTracker();
        _session = new NativeOverlayHookSessionMachine();
        _renderer = new NativeOverlayD3DRendererBridge();
        _capture = new NativeOverlayCaptureSessionBridge();

        _tracker.TargetChanged += OnTargetChanged;
        _session.StateChanged += OnSessionStateChanged;
    }

    public NativeHookSessionState SessionState => _session.State;
    public NativeOverlayTargetSnapshot? CurrentTarget => _session.CurrentTarget;

    public void Start()
    {
        if (_disposed || _isStarted) return;
        _isStarted = true;

        _session.Start(_probe.IsReady);
        if (!_probe.IsReady)
            return;

        if (!_renderer.TryInitialize(out var initReason))
        {
            _session.TriggerFallback("renderer-init-failed: " + initReason);
            return;
        }

        _tracker.Start();
        _frameTimer = new Timer(FrameTick, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public void Stop()
    {
        if (!_isStarted) return;
        _isStarted = false;

        _frameTimer?.Dispose();
        _frameTimer = null;

        _tracker.Stop();
        _capture.Stop();
        _renderer.DetachTarget();
        _session.Stop();
    }

    public void UpdateDesiredState(NativeOverlayDesiredState desired)
    {
        if (_desiredState.Equals(desired)) return;
        _desiredState = desired;

        App.Logger?.Debug(
            "Native host desired state updated: Pink={Pink}/{PinkOpacity}, Spiral={Spiral}/{SpiralOpacity}, BrainDrain={BrainDrain}/{BrainDrainIntensity}, Dual={Dual}",
            desired.PinkEnabled,
            desired.PinkOpacity,
            desired.SpiralEnabled,
            desired.SpiralOpacity,
            desired.BrainDrainEnabled,
            desired.BrainDrainIntensity,
            desired.DualMonitorEnabled);

        if (_session.State == NativeHookSessionState.Attached && _session.CurrentTarget.HasValue)
        {
            // Topology/overlay state changed while attached - refresh bridge attach cycle.
            TryAttachRuntime(_session.CurrentTarget.Value, "desired-state-change");
        }
    }

    public void NotifyTopWindowClosed()
    {
        if (!_isStarted) return;
        App.Logger?.Debug("Native host notified of top-window close; session={State}", _session.State);
    }

    private void OnTargetChanged(NativeOverlayTargetSnapshot? target)
    {
        _session.OnTargetChanged(target);

        if (!target.HasValue)
        {
            _capture.Stop();
            _renderer.DetachTarget();
            _session.MarkDetached("target-lost");
            return;
        }

        var t = target.Value;
        App.Logger?.Debug(
            "Native host target: pid={Pid}, name={Name}, screen={Screen}, attachReady={AttachReady}",
            t.ProcessId,
            t.ProcessName ?? "unknown",
            t.ScreenDeviceName ?? "unknown",
            t.IsAttachReady);

        TryAttachRuntime(t, "target-changed");
    }

    private void TryAttachRuntime(NativeOverlayTargetSnapshot target, string reason)
    {
        if (!_isStarted) return;

        _capture.Stop();
        _renderer.DetachTarget();

        if (!_renderer.TryAttachTarget(target, out var attachReason))
        {
            _session.MarkAttachFailed($"renderer-attach-failed: {attachReason} ({reason})", fallback: true);
            return;
        }

        if (!_capture.TryStart(target, out var captureReason))
        {
            _session.MarkAttachFailed($"capture-start-failed: {captureReason} ({reason})", fallback: true);
            return;
        }

        _session.MarkAttachSucceeded("native-runtime-attached");
    }

    private void FrameTick(object? _)
    {
        if (!_isStarted || _session.State != NativeHookSessionState.Attached)
            return;

        if (!_renderer.IsHealthy(out var rendererHealth))
        {
            _session.TriggerFallback("renderer-unhealthy: " + rendererHealth);
            return;
        }

        if (!_capture.IsHealthy(out var captureHealth))
        {
            _session.TriggerFallback("capture-unhealthy: " + captureHealth);
            return;
        }

        if (!_capture.TryAcquireFrame(out var frameReason))
        {
            _session.TriggerFallback("capture-frame-failed: " + frameReason);
            return;
        }

        if (!_renderer.TryRenderFrame(_desiredState, out var renderReason))
        {
            _session.TriggerFallback("render-failed: " + renderReason);
        }
    }

    private static void OnSessionStateChanged(NativeHookSessionState oldState, NativeHookSessionState newState, string reason)
    {
        App.Logger?.Information("Native hook session state: {Old} -> {New} ({Reason})", oldState, newState, reason);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _tracker.TargetChanged -= OnTargetChanged;
        _session.StateChanged -= OnSessionStateChanged;
        _tracker.Dispose();
        _capture.Dispose();
        _renderer.Dispose();
    }
}
