using System;

namespace ConditioningControlPanel.Services;

public sealed class NativeOverlayService : IOverlayService
{
    private readonly OverlayService _fallback;
    private readonly NativeOverlayProbeResult _probe;
    private readonly bool _fallbackOnly;
    private readonly NativeOverlayRuntimeHost? _host;
    private bool _loggedStart;

    public NativeOverlayService()
    {
        _fallback = new OverlayService();
        _probe = NativeOverlayBootstrap.Probe();
        _fallbackOnly = !_probe.IsReady;

        foreach (var warning in _probe.Warnings)
            App.Logger?.Warning("Native overlay probe warning: {Warning}", warning);

        if (_fallbackOnly)
        {
            foreach (var error in _probe.Errors)
                App.Logger?.Error("Native overlay probe failed: {Error}", error);
        }
        else
        {
            App.Logger?.Information(
                "Native overlay bootstrap ready (attachReady={AttachReady}, dwm={DwmEnabled})",
                _probe.ProcessAttachReady,
                _probe.DwmCompositionEnabled);

            _host = new NativeOverlayRuntimeHost(_probe);
        }
    }

    private void LogStartOnce()
    {
        if (_loggedStart) return;
        _loggedStart = true;

        if (_fallbackOnly)
        {
            App.Logger?.Warning("Native overlay backend unavailable; using WPF fallback backend");
            return;
        }

        App.Logger?.Information("Native overlay bootstrap is active; render path currently routed through WPF compatibility backend");
    }

    public bool IsRunning => _fallback.IsRunning;
    public bool BypassLevelCheck
    {
        get => _fallback.BypassLevelCheck;
        set => _fallback.BypassLevelCheck = value;
    }

    public void Start()
    {
        LogStartOnce();
        _host?.Start();
        SyncDesiredState();
        _fallback.Start();
    }

    public void Stop()
    {
        _host?.Stop();
        _fallback.Stop();
    }

    public void RefreshOverlays()
    {
        SyncDesiredState();
        _fallback.RefreshOverlays();
    }

    public void RefreshForDualMonitorChange()
    {
        SyncDesiredState();
        _fallback.RefreshForDualMonitorChange();
    }

    public void PulseOverlays() => _fallback.PulseOverlays();
    public void ShowOverlayTimed(string kind, int durationMs, double opacity) => _fallback.ShowOverlayTimed(kind, durationMs, opacity);
    public void ShowOverlaySustained(string kind, double opacity) => _fallback.ShowOverlaySustained(kind, opacity);
    public void HideOverlaySustained(string kind) => _fallback.HideOverlaySustained(kind);
    public void NotifyTopWindowClosed()
    {
        _host?.NotifyTopWindowClosed();
        _fallback.NotifyTopWindowClosed();
    }

    public void StopPinkFilter()
    {
        _fallback.StopPinkFilter();
        SyncDesiredState();
    }

    public void StopSpiral()
    {
        _fallback.StopSpiral();
        SyncDesiredState();
    }

    public void UpdateBrainDrainBlurOpacity(int intensity)
    {
        _fallback.UpdateBrainDrainBlurOpacity(intensity);
        SyncDesiredState();
    }

    public void WarmSpiralCache() => _fallback.WarmSpiralCache();

    public void SetSustainedOverlayOpacity(string kind, double opacity)
        => _fallback.SetSustainedOverlayOpacity(kind, opacity);

    public void Dispose()
    {
        _host?.Dispose();
        _fallback.Dispose();
    }

    private void SyncDesiredState()
    {
        if (_host == null) return;

        var settings = App.Settings?.Current;
        if (settings == null) return;

        _host.UpdateDesiredState(new NativeOverlayDesiredState(
            PinkEnabled: settings.PinkFilterEnabled,
            PinkOpacity: settings.PinkFilterOpacity,
            SpiralEnabled: settings.SpiralEnabled,
            SpiralOpacity: settings.SpiralOpacity,
            BrainDrainEnabled: settings.BrainDrainEnabled,
            BrainDrainIntensity: settings.BrainDrainIntensity,
            DualMonitorEnabled: settings.DualMonitorEnabled));
    }
}
