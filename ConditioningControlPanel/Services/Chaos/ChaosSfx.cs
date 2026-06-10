using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// One-shot sound effects for Chaos Mode (wave-clear, boon-reveal). A thin standalone
/// NAudio player — these fire rarely (a few per wave boundary), so no device pooling.
///
/// Each cue resolves with an override-then-fallback list: drop a dedicated file under
/// <c>Resources/sounds/chaos/</c> (e.g. <c>dling.mp3</c>) and it wins automatically; until
/// then it falls back to an existing bundled sound. Paths resolve through
/// <see cref="ModResourceResolver.ResolveAudioPath"/> so active mods can override too.
/// </summary>
public static class ChaosSfx
{
    /// <summary>Wave-cleared cue (a rewarding level-up chime) as the field pops.</summary>
    public static void PlayWaveClear() =>
        PlayFirstAvailable(new[] { "chaos/wave_clear.mp3", "lvup.mp3" }, 0.8f);

    /// <summary>Per-card boon reveal: a bright "dling" for rare, a dull "thud" otherwise.</summary>
    public static void PlayBoonReveal(bool isRare) =>
        PlayFirstAvailable(
            isRare ? new[] { "chaos/dling.mp3", "chime1.mp3" }
                   : new[] { "chaos/thud.mp3", "bubbles/Pop2.mp3" },
            isRare ? 0.6f : 0.65f);

    /// <summary>Confirmation cue when a boon is committed.</summary>
    public static void PlayBoonPicked() =>
        PlayFirstAvailable(new[] { "chaos/boon_pick.mp3", "chime2.mp3" }, 0.7f);

    /// <summary>Pendulum capstone: tick-tock underlay as slow-mo lands (silent until the asset ships).</summary>
    public static void PlayTickTock() =>
        PlayFirstAvailable(new[] { "chaos/ticktock.mp3" }, 0.45f);

    /// <summary>Generic one-shot cue: plays <c>Resources/sounds/chaos/{name}.mp3</c> if it exists
    /// (mod-overridable, silent no-op otherwise). For rare moments — high-frequency cues (pops,
    /// snaps) should go through <see cref="BubbleService.PlayCue"/>'s pooled devices instead.</summary>
    public static void Play(string name, float scale = 0.6f) =>
        PlayFirstAvailable(new[] { $"chaos/{name}.mp3" }, scale);

    /// <summary>Resolve a chaos cue to an absolute path for the pooled bubble player
    /// (empty string when the asset is absent).</summary>
    public static string ResolvePath(string name)
    {
        try
        {
            var path = ModResourceResolver.ResolveAudioPath($"chaos/{name}.mp3");
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : "";
        }
        catch { return ""; }
    }

    /// <summary>Resolve the first candidate that exists on disk and play it once.</summary>
    private static void PlayFirstAvailable(string[] candidates, float scale)
    {
        try
        {
            foreach (var rel in candidates)
            {
                string path;
                try { path = ModResourceResolver.ResolveAudioPath(rel); }
                catch { continue; }
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    PlayAsync(path, Volume(scale));
                    return;
                }
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosSfx resolve failed: {E}", ex.Message); }
    }

    private static float Volume(float scale)
    {
        try
        {
            float master = App.Settings.Current.MasterVolume / 100f;
            return Math.Clamp(master * scale, 0f, 1f);
        }
        catch { return scale; }
    }

    private static void PlayAsync(string path, float volume)
    {
        Task.Run(() =>
        {
            WaveOutEvent? outputDevice = null;
            AudioFileReader? audioFile = null;
            try
            {
                audioFile = new AudioFileReader(path) { Volume = volume };
                outputDevice = new WaveOutEvent();
                App.Audio?.ApplyPreferredDevice(outputDevice);   // honour the user's output device
                outputDevice.Init(audioFile);
                outputDevice.Play();
                while (outputDevice.PlaybackState == PlaybackState.Playing)
                    Thread.Sleep(40);
            }
            catch (Exception ex) { App.Logger?.Warning("ChaosSfx playback failed: {E}", ex.Message); }
            finally
            {
                try { outputDevice?.Dispose(); } catch { }
                try { audioFile?.Dispose(); } catch { }
            }
        });
    }
}
