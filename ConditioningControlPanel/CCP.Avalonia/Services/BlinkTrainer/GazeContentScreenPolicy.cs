using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Webcam;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.BlinkTrainer;

/// <summary>
/// Spawn-time placement clamp for gaze-reactive content. Baseline content (flashes, bubbles)
/// does NOT consult this — it spawns freely and the gaze read pipeline filters off-cal-screen
/// targets at the input side.
/// </summary>
public static class GazeContentScreenPolicy
{
    public static IReadOnlyList<ScreenInfo> ResolveGazeReactiveScreens(
        AppSettings? settings,
        IWebcamService webcam,
        IScreenProvider screens)
    {
        if (settings != null && settings.RestrictGazeContentToCalibratedScreen)
        {
            var cal = webcam.GetCalibratedScreen();
            if (cal != null) return new[] { cal };
        }

        if (settings != null && settings.DualMonitorEnabled)
            return screens.GetAllScreens();

        var primary = screens.GetPrimaryScreen();
        return primary != null ? new[] { primary } : Array.Empty<ScreenInfo>();
    }
}
