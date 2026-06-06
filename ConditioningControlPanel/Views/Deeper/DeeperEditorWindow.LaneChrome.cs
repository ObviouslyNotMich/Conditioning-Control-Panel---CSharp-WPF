using System.Globalization;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Views.Deeper
{
    // Mission 1 commit 4 — lane chrome bookkeeping.
    // Updates the per-lane item counts in the header column. The actual
    // shapes still render onto the single TimelineCanvas with the existing
    // y-band layout; a follow-up mission will split rendering into per-lane
    // canvases with independent collapse + resize.
    public partial class DeeperEditorWindow
    {
        // The timeline canvas is split into three equal horizontal lanes, top→bottom:
        // Regions, Effects, Haptics. Rules are NOT a lane — they render as full-height
        // pins layered across all three. This is the single source of truth for lane
        // geometry; every Build*/hit-test path resolves its Y band through LaneBand().
        private enum TimelineLane { Regions, Effects, Haptics }

        private static (double top, double height) LaneBand(TimelineLane lane, double canvasHeight)
        {
            if (canvasHeight <= 0) return (0, 0);
            double laneH = canvasHeight / 3.0;
            double top = lane switch
            {
                TimelineLane.Regions => 0,
                TimelineLane.Effects => laneH,
                TimelineLane.Haptics => 2 * laneH,
                _ => 0
            };
            return (top, laneH);
        }

        // Inset band rect for a lane (a few px of breathing room above/below so
        // adjacent lanes read as distinct). Used by region/haptic/effect-segment bands.
        private const double LaneInset = 2.0;
        private static (double top, double height) LaneBandInset(TimelineLane lane, double canvasHeight)
        {
            var (top, height) = LaneBand(lane, canvasHeight);
            return (top + LaneInset, System.Math.Max(0, height - 2 * LaneInset));
        }

        private void RefreshLaneCounts()
        {
            try
            {
                int regions = _enhancement?.Regions?.Count ?? 0;
                int haptics = 0;
                if (_enhancement?.HapticTracks != null)
                {
                    foreach (var t in _enhancement.HapticTracks)
                        if (t?.Events != null) haptics += t.Events.Count;
                }
                int effects = 0;
                if (_enhancement?.TimelineItems != null)
                {
                    foreach (var ti in _enhancement.TimelineItems)
                        if (ti != null && ti.Kind == TimelineItemKind.Effect &&
                            ti.EffectType != EffectTypes.Haptic) effects++;
                }

                if (TxtRegionsLaneCount != null)
                    TxtRegionsLaneCount.Text = regions == 0 ? "" : regions.ToString(CultureInfo.InvariantCulture);
                if (TxtEffectsLaneCount != null)
                    TxtEffectsLaneCount.Text = effects == 0 ? "" : effects.ToString(CultureInfo.InvariantCulture);
                if (TxtHapticsLaneCount != null)
                    TxtHapticsLaneCount.Text = haptics == 0 ? "" : haptics.ToString(CultureInfo.InvariantCulture);
            }
            catch { }
        }
    }
}
