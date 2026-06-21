using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services;

/// <summary>
/// Helpers for migrating legacy keyword-trigger storage shapes to the current model.
/// </summary>
public static class KeywordTriggerMigrationHelper
{
    /// <summary>
    /// Rebuild the <see cref="KeywordTrigger.Actions"/> list from the legacy flat
    /// fields. Used by settings load-time migration and by editors that still expose
    /// the flat-field UI.
    /// </summary>
    public static void RebuildActionsFromFlatFields(KeywordTrigger trigger)
    {
        if (trigger == null) return;

        var list = new List<KeywordAction>();

        if (!string.IsNullOrEmpty(trigger.AudioFilePath))
        {
            list.Add(new PlayAudioAction
            {
                FilePath = trigger.AudioFilePath,
                Volume = trigger.AudioVolume,
                PlayCount = trigger.AudioPlayCount,
                DelayBetweenMs = trigger.AudioDelayBetweenMs,
                DuckSystemAudio = trigger.DuckAudio,
            });
        }

        if (trigger.VisualEffect != KeywordVisualEffect.None &&
            trigger.VisualEffect != KeywordVisualEffect.HighlightOnly)
        {
            list.Add(new VisualEffectAction { Effect = trigger.VisualEffect });
        }

        // Always include Highlight — it self-guards on matchedWords != null & global setting.
        list.Add(new HighlightAction());

        if (trigger.HapticEnabled)
            list.Add(new HapticAction { Intensity = trigger.HapticIntensity });

        if (trigger.XPAward > 0)
            list.Add(new AddXpAction { Amount = trigger.XPAward });

        trigger.Actions = list;
    }
}
