using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Story;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The director for a story "popping session": a deterministic, song-driven Chaos descent launched by
/// the in-app VN runner. Mirrors <see cref="ChaosHappyPath"/> (a static, run-scoped director the run
/// ticks into) but instead of teaching beats it:
///   • plays the scene's song through a <see cref="MusicService"/> and shows the scene's backdrop,
///   • drives the live spawn rate (<c>SpawnRateMult</c>) off the song's loudness envelope every tick —
///     so the bubbles swell and ease with the music — with optional authored keyframe overrides,
///   • fires scripted events (overwhelm / announce / sfx / cut_short) at exact song timestamps.
/// Boon drafts, curses and meta-progression are all OFF (see <see cref="BuildConfig"/>), so the run is
/// repeatable and serves the narrative. All state is run-scoped and cleared by <see cref="OnRunEnded"/>;
/// every path swallows its own exceptions — the script must never hurt a run.
/// </summary>
public static class ChaosMusicalDirector
{
    // Fixed, non-intrusive bubble pool for story sessions (no video/htlink payloads yanking the player).
    private static readonly List<string> StoryVariants = new() { "flash", "subliminal", "pink", "spiral" };

    private static ChaosRunState? _state;
    private static ChaosModeService? _svc;
    private static MusicService? _music;

    private static PoppingSession? _session;
    private static SongEnvelope? _env;
    private static string? _backgroundPath;
    private static string? _songPath;

    private static List<SpawnKeyframe> _keyframes = new();
    private static List<SessionEvent> _events = new();
    private static int _nextEvent;
    private static double _overwhelmUntilSec;
    private static double _overwhelmMult = 1.0;
    private static bool _stopRequested;

    /// <summary>True while a story popping session is driving a run (narrative layer is skipped for it,
    /// but other systems can consult this to stand down).</summary>
    public static bool IsActive => _state != null;

    /// <summary>
    /// Build the deterministic run config for a popping session and stash everything the run will need.
    /// Called by <see cref="ChaosModeService.StartStoryRun"/> BEFORE StartRun. The background/song/envelope
    /// paths are pre-resolved by the caller (the VN runner already resolves story assets).
    /// </summary>
    public static ChaosRunConfig Prepare(PoppingSession session, string? backgroundPath, string songPath, string? envelopePath)
    {
        _session = session;
        _backgroundPath = backgroundPath;
        _songPath = songPath;
        _env = !string.IsNullOrEmpty(envelopePath) ? SongEnvelope.Load(envelopePath!) : null;
        _keyframes = (session.SpawnKeyframes ?? new()).OrderBy(k => k.T).ToList();
        _events = (session.Events ?? new()).OrderBy(e => e.T).ToList();
        _nextEvent = 0;
        _overwhelmUntilSec = 0; _overwhelmMult = 1.0;
        _stopRequested = false;

        int durationSec = session.DurationSec > 0 ? session.DurationSec : ProbeSongLengthSec(songPath);
        durationSec = Math.Clamp(durationSec, 15, 900);
        var diff = Enum.TryParse<ChaosDifficulty>(session.Difficulty, ignoreCase: true, out var d) ? d : ChaosDifficulty.Medium;

        return new ChaosRunConfig
        {
            ScriptedStoryRun = true,
            Difficulty = diff,
            DurationSec = durationSec,
            WaveCount = Math.Clamp((int)Math.Round(durationSec / 60.0), 1, 12),
            EnabledVariants = new List<string>(StoryVariants),
            BoonDraftEnabled = false,
            AllowCurses = false,
            DartersEnabled = false,
            SinChance = 0.0,
            // Director owns the spawn rate from tick 1; this is just the pre-tick seed.
            SpawnRateMult = 1.0,
        };
    }

    /// <summary>BeginRun calls this for a story run (in place of the narrative + depth-backdrop setup):
    /// show the scene backdrop and start the song.</summary>
    public static void OnRunStarted(ChaosRunState state, ChaosModeService svc)
    {
        _state = state;
        _svc = svc;
        try
        {
            if (!string.IsNullOrEmpty(_backgroundPath))
                ChaosBackdropService.ShowCustom(_backgroundPath!);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosMusicalDirector backdrop: {E}", ex.Message); }

        try
        {
            if (!string.IsNullOrEmpty(_songPath))
            {
                _music ??= new MusicService(App.Audio);
                int vol = App.Settings?.Current?.MasterVolume ?? 100;
                _music.Play(_songPath!, loop: _session?.Loop ?? false, volumePercent: vol);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosMusicalDirector music: {E}", ex.Message); }
    }

    /// <summary>EndRun / teardown: stop the song, drop references. Idempotent.</summary>
    public static void OnRunEnded()
    {
        try { _music?.Stop(); } catch { }
        _music = null;
        _state = null;
        _svc = null;
        _session = null;
        _env = null;
        _backgroundPath = _songPath = null;
        _keyframes = new(); _events = new();
        _nextEvent = 0; _overwhelmUntilSec = 0; _overwhelmMult = 1.0; _stopRequested = false;
    }

    /// <summary>Driven from the 250ms run tick (already gated on spawning + unpaused). No-op unless a
    /// story session is active.</summary>
    public static void Tick(double dt)
    {
        var s = _state;
        if (s == null || _session == null) return;
        try
        {
            // Song time drives everything. Fall back to run-elapsed if the track failed to load.
            double t = (_music?.IsPlaying == true) ? _music.Position.TotalSeconds : s.ElapsedSec;

            // 1) spawn rate: authored keyframe curve if present, else the loudness envelope baseline.
            double mult = _keyframes.Count > 0
                ? SampleKeyframes(t)
                : Lerp(_session.SpawnFloor, _session.SpawnCeil, _env?.SampleAt(t) ?? 0.5);
            if (t < _overwhelmUntilSec) mult *= _overwhelmMult;
            s.Config.SpawnRateMult = Math.Clamp(mult, 0.1, 10.0);

            // 2) scripted events (sorted by t): fire each once when the song passes it.
            while (_nextEvent < _events.Count && _events[_nextEvent].T <= t)
            {
                FireEvent(_events[_nextEvent], s);
                _nextEvent++;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosMusicalDirector.Tick: {E}", ex.Message); }
    }

    private static void FireEvent(SessionEvent e, ChaosRunState s)
    {
        switch ((e.Type ?? "").ToLowerInvariant())
        {
            case "overwhelm":
                // Flood: boost spawn for a window. The Tick spawn math multiplies by this while active.
                _overwhelmMult = Math.Clamp(e.Intensity, 1.0, 6.0);
                _overwhelmUntilSec = ((_music?.IsPlaying == true) ? _music.Position.TotalSeconds : s.ElapsedSec) + Math.Max(0.5, e.DurSec);
                ChaosAnnouncerOverlay.Announce(string.IsNullOrEmpty(e.Text) ? "they're trying to drown you" : e.Text!,
                    ChaosAnnounceKind.Temptation);
                s.PushEvent("⚠ the bubbles surge");
                break;

            case "announce":
                var kind = Enum.TryParse<ChaosAnnounceKind>(e.Kind, ignoreCase: true, out var k) ? k : ChaosAnnounceKind.Depth;
                if (!string.IsNullOrEmpty(e.Text))
                    ChaosAnnouncerOverlay.Announce(e.Text!, kind);
                break;

            case "sfx":
                if (!string.IsNullOrEmpty(e.Cue))
                    ChaosSfx.Play(e.Cue!, (float)Math.Clamp(e.Vol, 0.0, 1.0));
                break;

            case "cut_short":
                // End the run early — but NOT mid-tick (RunTick still dereferences _state after us).
                if (!_stopRequested)
                {
                    _stopRequested = true;
                    var svc = _svc;
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => { try { svc?.RequestStop(); } catch { } }));
                }
                break;

            default:
                App.Logger?.Debug("ChaosMusicalDirector: unknown event type '{T}'", e.Type);
                break;
        }
    }

    private static double SampleKeyframes(double t)
    {
        var ks = _keyframes;
        if (ks.Count == 0) return 1.0;
        if (t <= ks[0].T) return ks[0].Mult;
        if (t >= ks[^1].T) return ks[^1].Mult;
        for (int i = 0; i < ks.Count - 1; i++)
        {
            if (t >= ks[i].T && t <= ks[i + 1].T)
            {
                double span = ks[i + 1].T - ks[i].T;
                double f = span <= 0 ? 0 : (t - ks[i].T) / span;
                return Lerp(ks[i].Mult, ks[i + 1].Mult, f);
            }
        }
        return ks[^1].Mult;
    }

    private static double Lerp(double a, double b, double f) => a + (b - a) * Math.Clamp(f, 0, 1);

    private static int ProbeSongLengthSec(string path)
    {
        try
        {
            using var r = new NAudio.Wave.AudioFileReader(path);
            return (int)Math.Ceiling(r.TotalTime.TotalSeconds);
        }
        catch { return 120; }
    }
}
