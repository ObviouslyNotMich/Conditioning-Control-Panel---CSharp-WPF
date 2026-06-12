using System;

namespace ConditioningControlPanel.Services;

public interface IOverlayService : IDisposable
{
    bool IsRunning { get; }
    bool BypassLevelCheck { get; set; }

    void Start();
    void Stop();
    void RefreshOverlays();
    void RefreshForDualMonitorChange();
    void PulseOverlays();
    void ShowOverlayTimed(string kind, int durationMs, double opacity);
    void ShowOverlaySustained(string kind, double opacity);
    void HideOverlaySustained(string kind);
    void NotifyTopWindowClosed();

    // Pre-decode spiral assets off-thread so the first show doesn't hitch.
    void WarmSpiralCache();

    // Live opacity update for an already-shown sustained overlay.
    void SetSustainedOverlayOpacity(string kind, double opacity);

    // Legacy control points still used by existing callers.
    void StopPinkFilter();
    void StopSpiral();
    void UpdateBrainDrainBlurOpacity(int intensity);
}
