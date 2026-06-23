using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// One beat of the Rabbit Hole opening, authored by the Python VN editor (<c>vn_editor.py</c>)
    /// into <c>assets/story/opening.json</c>. A beat is normally a dialogue/caption frame (background +
    /// positioned characters + a speech bubble). When <see cref="BeatType"/> is <c>popping_session</c>
    /// it carries a <see cref="Session"/> block that turns the beat into a song-synced Chaos run.
    /// Field names mirror the editor's JSON exactly (snake_case via JsonProperty).
    /// </summary>
    public sealed class StoryBeat
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("scene")] public string Scene { get; set; } = "";
        [JsonProperty("speaker")] public string Speaker { get; set; } = "";
        [JsonProperty("text")] public string Text { get; set; } = "";
        [JsonProperty("background")] public string? Background { get; set; }

        /// <summary>"dialogue" / "caption" (or absent) = a VN beat; "popping_session" = a Chaos run.</summary>
        [JsonProperty("beat_type")] public string? BeatType { get; set; }

        [JsonProperty("bubble")] public BubblePos Bubble { get; set; } = new();
        [JsonProperty("characters")] public List<StoryCharacter> Characters { get; set; } = new();

        /// <summary>Present only on a popping-session beat.</summary>
        [JsonProperty("session")] public PoppingSession? Session { get; set; }

        [JsonIgnore] public bool IsPoppingSession =>
            string.Equals(BeatType, "popping_session", StringComparison.OrdinalIgnoreCase) && Session != null;
    }

    public sealed class BubblePos
    {
        [JsonProperty("cx")] public double Cx { get; set; } = 0.5;
        [JsonProperty("cy")] public double Cy { get; set; } = 0.86;
    }

    public sealed class StoryCharacter
    {
        [JsonProperty("image")] public string? Image { get; set; }
        [JsonProperty("cx")] public double Cx { get; set; } = 0.5;
        [JsonProperty("cy")] public double Cy { get; set; } = 0.62;
        [JsonProperty("h")] public double H { get; set; } = 0.94;
        [JsonProperty("flip")] public bool Flip { get; set; }
    }

    /// <summary>The gameplay block of a popping-session beat: the song, how spawn tracks it, and the
    /// scripted events fired at song timestamps. Consumed by <c>ChaosMusicalDirector</c>.</summary>
    public sealed class PoppingSession
    {
        [JsonProperty("song")] public string Song { get; set; } = "";
        [JsonProperty("envelope")] public string? Envelope { get; set; }
        [JsonProperty("difficulty")] public string Difficulty { get; set; } = "Medium";
        /// <summary>0 = run for the song's full length.</summary>
        [JsonProperty("duration_sec")] public int DurationSec { get; set; }
        [JsonProperty("loop")] public bool Loop { get; set; }

        /// <summary>The envelope value 0..1 maps linearly into [<see cref="SpawnFloor"/>, <see cref="SpawnCeil"/>]
        /// to become the run's <c>SpawnRateMult</c> baseline ("bubbles swell with the music").</summary>
        [JsonProperty("spawn_floor")] public double SpawnFloor { get; set; } = 0.4;
        [JsonProperty("spawn_ceil")] public double SpawnCeil { get; set; } = 1.6;

        /// <summary>Optional hand-authored overrides on top of the envelope baseline (lerped between).</summary>
        [JsonProperty("spawn_keyframes")] public List<SpawnKeyframe> SpawnKeyframes { get; set; } = new();

        /// <summary>Discrete events fired once when song time passes their <c>t</c>.</summary>
        [JsonProperty("events")] public List<SessionEvent> Events { get; set; } = new();
    }

    public sealed class SpawnKeyframe
    {
        [JsonProperty("t")] public double T { get; set; }
        [JsonProperty("mult")] public double Mult { get; set; } = 1.0;
    }

    /// <summary>A scripted gameplay event. <see cref="Type"/> selects which optional fields matter:
    /// overwhelm (Intensity, DurSec), announce (Text, Kind), sfx (Cue, Vol), cut_short (none).</summary>
    public sealed class SessionEvent
    {
        [JsonProperty("t")] public double T { get; set; }
        [JsonProperty("type")] public string Type { get; set; } = "";
        [JsonProperty("text")] public string? Text { get; set; }
        [JsonProperty("kind")] public string? Kind { get; set; }
        [JsonProperty("cue")] public string? Cue { get; set; }
        [JsonProperty("vol")] public double Vol { get; set; } = 0.6;
        [JsonProperty("intensity")] public double Intensity { get; set; } = 1.8;
        [JsonProperty("dur_sec")] public double DurSec { get; set; } = 6;
    }

    /// <summary>The whole opening: an ordered list of beats. Root of <c>opening.json</c>.</summary>
    public sealed class StoryScript
    {
        [JsonProperty("beats")] public List<StoryBeat> Beats { get; set; } = new();

        public static StoryScript? Load(string path)
        {
            try
            {
                if (!File.Exists(path)) { App.Logger?.Warning("StoryScript: missing {Path}", path); return null; }
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<StoryScript>(json);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "StoryScript.Load failed for {Path}", path);
                return null;
            }
        }
    }

    /// <summary>A baked loudness envelope + beat grid for one song, produced offline by
    /// <c>analyze_song.py</c>. The app never does FFT at runtime — it samples this.</summary>
    public sealed class SongEnvelope
    {
        [JsonProperty("hopSec")] public double HopSec { get; set; } = 0.05;
        [JsonProperty("values")] public List<double> Values { get; set; } = new();
        [JsonProperty("bpm")] public double Bpm { get; set; }
        [JsonProperty("beatTimesSec")] public List<double> BeatTimesSec { get; set; } = new();

        public static SongEnvelope? Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<SongEnvelope>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("SongEnvelope.Load failed for {Path}: {E}", path, ex.Message);
                return null;
            }
        }

        /// <summary>Linearly-interpolated envelope value (0..1) at the given song-seconds. Returns a
        /// neutral 0.5 when there is no data so spawn lands mid-band rather than dead.</summary>
        public double SampleAt(double seconds)
        {
            int n = Values.Count;
            if (n == 0 || HopSec <= 0) return 0.5;
            double idx = seconds / HopSec;
            if (idx <= 0) return Math.Clamp(Values[0], 0, 1);
            if (idx >= n - 1) return Math.Clamp(Values[n - 1], 0, 1);
            int i = (int)idx;
            double frac = idx - i;
            return Math.Clamp(Values[i] * (1 - frac) + Values[i + 1] * frac, 0, 1);
        }
    }
}
