using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Core.Services.Deeper;

/// <summary>
/// Seam for the Deeper <c>speak</c> effect — drives one "say X for me" voice prompt: shows an
/// on-screen cue, opens the offline recognizer, scores what the user says against the target, and
/// flashes correct/incorrect feedback. Core's dispatcher starts/stops it per band; the head owns the
/// cue UI + recognizer. Null default = no-op (no speech engine), so the effect self-skips.
/// </summary>
public interface ISpeakPromptHost
{
    /// <summary>Begin a speak prompt for the band identified by <paramref name="effect"/>.EffectId.</summary>
    void StartSpeak(TriggerEffectAction effect, IPlaybackTimeSource? source);

    /// <summary>Stop the speak prompt for the given band (band exit / effect stop). Null = stop all.</summary>
    void StopSpeak(string? effectId);
}

/// <summary>Default no-op host (no speech engine). The Avalonia head overrides this.</summary>
public sealed class NullSpeakPromptHost : ISpeakPromptHost
{
    public void StartSpeak(TriggerEffectAction effect, IPlaybackTimeSource? source) { }
    public void StopSpeak(string? effectId) { }
}
