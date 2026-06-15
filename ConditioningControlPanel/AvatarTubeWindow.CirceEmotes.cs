using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using XamlAnimatedGif;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Data-driven animated emote layer for built-in companion poses. When the active mod + current
    /// avatar set match an entry in <c>Resources/avatar_emotes_registry.json</c>, the static/portrait
    /// avatar is replaced by transparent GIF clips from that entry's folder.
    ///
    /// Playback model (rotation, never loop): every clip plays exactly ONCE and then crossfades to the
    /// next — driven by the clip's AnimationCompleted (with a watchdog fallback). Idle is a weighted
    /// rotation of the idleRotation pool. A spoken line builds a short queue of [talking clip(s), sized
    /// by spoken length] followed by [one reaction emote, by situation], played in order, after which it
    /// returns to idle rotation. Two animated layers (A/B) crossfade so the discontinuity at a clip's
    /// wrap is never seen.
    ///
    /// To add a pose/mod: produce the clips (see tools/avatar-emotes/README.md), drop them in a new
    /// Resources/&lt;folder&gt;/ with an emotes.json, add one line to the registry, embed both in the
    /// csproj. No code changes here. The class name is historical ("Circe"); the logic is generic.
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private bool _circeEmoteMode;
        private bool _circeReacting;
        private string? _circeCurrentClip;
        private Image? _circeActiveImg; // ImgAvatarAnimated or ImgAvatarAnimatedB

        // The resource folder backing the currently-engaged emote set (e.g. "avatar0_emotes"), and the
        // folder whose emotes.json is currently parsed into the fields below (so a pose switch reloads).
        private string? _emoteFolder;
        private string? _loadedEmoteFolder;

        // Remaining clips of the current spoken-line sequence (talk... -> reaction). Drained by AdvanceCirce.
        private readonly Queue<string> _circeQueue = new();
        private bool _circeHandlersAttached;

        // Forces the next rotation if a clip's AnimationCompleted never arrives (load failure, etc.).
        private DispatcherTimer? _circeWatchdog;
        private const int CirceWatchdogMs = 13000; // clips ~8s + 1s fade + margin

        // Minimum on-screen time per clip: a swap requested sooner is coalesced and deferred, so no clip
        // ever flashes by faster than this (rapid back-to-back bark lines interrupting mid-clip).
        private const int CirceMinHoldMs = 2000;
        private long _circeClipStartTick;
        private string? _circePendingClip;
        private DispatcherTimer? _circeMinHoldTimer;

        // mapping (from Resources/<folder>/emotes.json)
        private int _circeFadeMs = 1000;  // crossfade dissolve between clips (emotes.json "fadeMs" overrides)
        private readonly List<(string clip, int weight)> _circeIdle = new();
        private readonly List<KeyValuePair<string, string>> _circeStemPrefix = new(); // longest-first
        private readonly Dictionary<string, string> _circeMoodMap = new(StringComparer.OrdinalIgnoreCase);
        // Per-clip extra scale multiplier (emotes.json "clipScale"), e.g. talkB at 0.9 = 10% smaller than
        // the rest. Applied on top of the layout scale, per clip, as it's crossfaded in.
        private readonly Dictionary<string, double> _circeClipScale = new(StringComparer.OrdinalIgnoreCase);
        // Talking is drawn at RANDOM from this pool (not fixed clips), so repeated speech varies. How many
        // clips a line uses is decided by their speaking-windows vs the voiceline length (see PlanTalk).
        private List<string> _talkPool = new() { "talkA", "talkC" };
        private double _talkLongMin = 9.0; // legacy; superseded by clip_timing windows (kept for back-compat)
        private int _maxTalkClips = 3;     // emotes.json talking.maxTalkClips: cap on chained talk clips

        // Pacing (emotes.json): the voice leads — a spoken line's audio fires immediately, the talking
        // animation joins _talkStartDelayMs into the line, and the reaction crossfades in _talkLeadOutMs
        // BEFORE the line ends, so the mouth never keeps moving after the voice has stopped. The visual
        // join is pushed back further if the current clip still needs its _minClipMs on screen (so
        // reactions/idles aren't cut short).
        private int _talkStartDelayMs = 1000;
        private int _talkLeadOutMs = 500;
        private int _minClipMs = 2500;

        // Non-verbal sounds (giggle/"mmm"/moans) and reaction-less lines (repetitive flash audio) play one
        // of these expressive emotes instead of / after talking, for variety. From emotes.json "expressive".
        private List<string> _expressiveEmotes = new() { "giggle", "sultry", "wink", "tease", "blowkiss", "adoring", "flirt", "shy" };
        private double _nonverbalMaxSec = 1.4;        // an untexted sound shorter than this = a quick vocalization
        private const int NonverbalLeadInMs = 250;    // expressive clips have no measured window — small lead

        // Per-clip speaking window (ms), from clip_timing.json: speakStart = when the mouth starts moving,
        // speakEnd = when it stops. Drives (a) how many talk clips cover a line and (b) the per-line audio
        // lead-in (fire the voice as the mouth opens). Missing clip -> TalkFallback* below.
        private readonly Dictionary<string, (int startMs, int endMs, int durMs)> _circeTiming =
            new(StringComparer.OrdinalIgnoreCase);
        private const int TalkFallbackStartMs = 600;   // ~the old flat lead-in
        private const int TalkFallbackLenMs = 2500;    // assumed talking length when a clip is untimed
        private const int MinTalkWindowMs = 500;       // shortest talk window worth showing mouth clips for

        // Audio lead-in for the CURRENT spoken line, read via EmoteAudioLeadInSeconds. Spoken lines are
        // voice-first (0 — the talking animation joins _talkStartDelayMs in instead of leading); only
        // non-verbal sounds still hold the audio back slightly so they land on the expressive emote.
        private int _circeNextAudioLeadInMs = 0;

        // Window-driven talk sequence: this timer fires the scheduled clip transitions (talk[i] -> talk[i+1]
        // -> reaction at the line's end), timed to the voiceline rather than the GIF length. While it's
        // active, AnimationCompleted does NOT drive rotation — the timer owns the talk transitions.
        private DispatcherTimer? _circeTalkTimer;
        private bool _circeTalkSeqActive;
        private readonly Queue<(long atMs, string clip, bool isReaction)> _talkSchedule = new();
        private long _talkSeqStartTick;

        // Clicking the avatar plays one of these "rare" affectionate emotes (emotes.json "clickEmotes"),
        // throttled by CirceClickCooldownMs so mashing doesn't spam it.
        private List<string> _circeClickEmotes = new() { "shy", "sultry", "adoring", "tender", "blowkiss" };
        private const int CirceClickCooldownMs = 3000;
        private long _circeClickCooldownTick = long.MinValue;
        // Clip names referenced by the loaded map (idles + talking + overrides + moods). Used to reject
        // typos/missing mappings; built per-folder so each pose may ship a different clip set.
        private readonly HashSet<string> _circeKnownClips = new(StringComparer.OrdinalIgnoreCase);

        // Optional per-emote-set layout adjustment (emotes.json "layout"), applied as a DELTA on top of
        // the mod's global TubeLayout so a pose can be nudged independently of the static avatar.
        // scale = multiplier (0.9 = 10% smaller); offsetX += right; offsetY += DOWN (intuitive signs).
        // Absent -> no adjustment (mod's TubeLayout unchanged).
        private bool _emoteHasLayout;
        private double _emoteScaleMul = 1.0;
        private int _emoteOffX, _emoteOffY, _emoteDetX, _emoteDetY;

        // (modId, avatarSet) -> folder, parsed once from avatar_emotes_registry.json.
        private static List<(string modId, int set, string folder)>? _emoteRegistry;

        /// <summary>The folder for the active (mod, set) emote pair, or null if none is registered.</summary>
        private string? ResolveEmoteFolder()
        {
            var modId = App.Mods?.ActiveModId;
            if (string.IsNullOrEmpty(modId)) return null;
            foreach (var e in LoadEmoteRegistry())
                if (string.Equals(e.modId, modId, StringComparison.OrdinalIgnoreCase) && e.set == _currentAvatarSet)
                    return e.folder;
            return null;
        }

        /// <summary>Registered emote sets for the active mod, ascending ([1] for BS/Sissy, [1,2,3,4] for Circe).</summary>
        private int[] EmoteSetsForActiveMod()
        {
            var modId = App.Mods?.ActiveModId;
            if (string.IsNullOrEmpty(modId)) return Array.Empty<int>();
            return LoadEmoteRegistry()
                .Where(e => string.Equals(e.modId, modId, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.set).Distinct().OrderBy(x => x).ToArray();
        }

        /// <summary>
        /// True for a mod whose ONLY avatar is one animated emote set — these drop the level picker /
        /// nav arrows entirely (BambiSleep, Sissy). Multi-set emote mods (Circe's 4 poses) and non-emote
        /// mods return false. When more animated levels are added for such a mod, it gains more registry
        /// entries → no longer single → the picker returns automatically.
        /// </summary>
        private bool IsSingleEmoteAvatarMod(out int set)
        {
            var sets = EmoteSetsForActiveMod();
            if (sets.Length == 1) { set = sets[0]; return true; }
            set = 0;
            return false;
        }

        private static List<(string modId, int set, string folder)> LoadEmoteRegistry()
        {
            if (_emoteRegistry != null) return _emoteRegistry;
            var list = new List<(string, int, string)>();
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/avatar_emotes_registry.json", UriKind.Absolute);
                var sri = Application.GetResourceStream(uri);
                if (sri != null)
                {
                    using var r = new System.IO.StreamReader(sri.Stream);
                    var j = JObject.Parse(r.ReadToEnd());
                    if (j["sets"] is JArray arr)
                        foreach (var it in arr)
                        {
                            var modId = (string?)it["modId"];
                            var set = (int?)it["avatarSet"];
                            var folder = (string?)it["folder"];
                            if (!string.IsNullOrEmpty(modId) && set.HasValue && !string.IsNullOrEmpty(folder))
                                list.Add((modId!, set.Value, folder!));
                        }
                }
                else App.Logger?.Warning("avatar_emotes_registry.json not found");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to load avatar_emotes_registry.json");
            }
            _emoteRegistry = list;
            return list;
        }

        /// <summary>Call after any avatar/mod/set setup to enter, leave, or switch the emote set.</summary>
        private void TryUpdateCirceEmoteMode()
        {
            try
            {
                var folder = ResolveEmoteFolder();
                if (!string.IsNullOrEmpty(folder))
                {
                    if (!_circeEmoteMode) EnterCirceEmoteMode(folder!);
                    else if (!string.Equals(folder, _emoteFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        // Switched to a different registered pose while already animating: re-engage.
                        LeaveCirceEmoteMode();
                        EnterCirceEmoteMode(folder!);
                    }
                    else
                    {
                        // Same folder, still engaged — e.g. BambiSleep <-> Sissy both map to
                        // avatar0_emotes. The mod-change/set-switch setup that ran before us has
                        // already re-shown the static avatar (or fully re-entered portrait mode when
                        // the new mod ships a manifest, like Sissy), which then sits visible BEHIND
                        // the still-running emote layers — the "double avatar" bug. Re-hide it.
                        ReassertCirceEmoteVisuals();
                    }
                }
                else if (_circeEmoteMode)
                {
                    LeaveCirceEmoteMode();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Emote mode toggle failed: {Error}", ex.Message);
            }
        }

        private void EnterCirceEmoteMode(string folder)
        {
            _emoteFolder = folder;
            if (!LoadCirceMap()) { _emoteFolder = null; return; }

            LeavePortraitMode();          // never run portrait + emote together
            _poseTimer.Stop();            // no legacy 4-pose rotation
            _useAnimatedAvatar = false;
            // The mod-change setup may have just entered portrait mode (the new mod ships a manifest),
            // whose chrome shifted AvatarBorder; LeavePortraitMode doesn't reset that transform.
            ApplyAvatarTransform(_currentAvatarSet);

            ImgAvatar.Visibility = Visibility.Collapsed;
            if (ImgAvatarB != null) ImgAvatarB.Visibility = Visibility.Collapsed;

            ImgAvatarAnimated.BeginAnimation(UIElement.OpacityProperty, null); // kill stale fade clocks
            ImgAvatarAnimatedB.BeginAnimation(UIElement.OpacityProperty, null);
            ClearGifLayer(ImgAvatarAnimated);
            ClearGifLayer(ImgAvatarAnimatedB);
            ImgAvatarAnimated.Opacity = 1;
            ImgAvatarAnimatedB.Opacity = 0;
            _circeActiveImg = ImgAvatarAnimated;

            // One-time: each layer drives the rotation when its clip finishes playing (plays once).
            if (!_circeHandlersAttached)
            {
                AnimationBehavior.AddAnimationCompletedHandler(ImgAvatarAnimated, OnCirceClipCompleted);
                AnimationBehavior.AddAnimationCompletedHandler(ImgAvatarAnimatedB, OnCirceClipCompleted);
                AnimationBehavior.AddErrorHandler(ImgAvatarAnimated, OnCirceGifError);
                AnimationBehavior.AddErrorHandler(ImgAvatarAnimatedB, OnCirceGifError);
                _circeHandlersAttached = true;
            }

            _circeReacting = false;
            _circeQueue.Clear();
            _circeCurrentClip = null;
            _circeEmoteMode = true;
            _circeWatchdog ??= CreateWatchdog();

            ApplyTubeLayoutOffsets();     // pick up this set's optional layout override

            // Defer the first heavy GIF render to a FRESH dispatcher tick. Engage is triggered
            // synchronously by a user click (mod selector / avatar arrows). In that same dispatcher
            // frame, the click also closes/resizes a layered popup (the mod-dropdown / a tooltip).
            // A layered popup's resize does a SYNCHRONOUS HwndTarget.OnResize → MediaContext.
            // CompleteRender() that blocks on the render thread; if the avatar's heavy GIF crossfade
            // is rendering on that same (shared, software/AllowsTransparency) render thread in the
            // same frame, the two deadlock and the whole app hangs ("not responding"). Running the
            // crossfade on a Background tick lets the popup's resize present finish first, so they
            // never collide. (Diagnosed 2026-06-10 from live hang dumps: UI thread stuck in
            // CompleteRender on every emote-mode mod/set switch.)
            var firstIdle = PickWeightedIdle();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                if (_circeEmoteMode && _circeCurrentClip == null) DoCirceCrossfade(firstIdle);
            }));
            App.Logger?.Information("Emote mode engaged ({Folder}, set {Set}).", folder, _currentAvatarSet);
        }

        private void LeaveCirceEmoteMode()
        {
            _circeEmoteMode = false;
            _circeReacting = false;
            _emoteFolder = null;
            _emoteHasLayout = false;
            _circeQueue.Clear();
            StopTalkSequence();
            _circeWatchdog?.Stop();
            _circeMinHoldTimer?.Stop();
            _circePendingClip = null;
            // Cancel any in-flight crossfade so the local opacity values below actually take effect —
            // a running fade's animated value otherwise overrides them until its clock ends.
            ImgAvatarAnimated.BeginAnimation(UIElement.OpacityProperty, null);
            ImgAvatarAnimatedB.BeginAnimation(UIElement.OpacityProperty, null);
            ClearGifLayer(ImgAvatarAnimated);
            ClearGifLayer(ImgAvatarAnimatedB);
            ImgAvatarAnimated.Opacity = 1;
            ImgAvatarAnimated.Visibility = Visibility.Collapsed;
            ImgAvatarAnimatedB.Opacity = 0;
            ImgAvatarAnimatedB.Visibility = Visibility.Collapsed;
            _circeCurrentClip = null;
            _circeActiveImg = null;
            ApplyTubeLayoutOffsets();     // revert to the mod's global TubeLayout
            // Caller restores the normal static/animated/portrait avatar after this returns.
        }

        /// <summary>
        /// Emote mode stayed engaged across a mod/set switch (same registered folder), but the normal
        /// avatar setup that ran first re-showed the static/portrait avatar and may have cleared the
        /// active GIF layer (every OnModChanged branch touches ImgAvatarAnimated). Put the emote-mode
        /// visuals back without restarting clips that are still playing.
        /// </summary>
        private void ReassertCirceEmoteVisuals()
        {
            if (!_circeEmoteMode) return;

            LeavePortraitMode();          // sissy's manifest re-engages portrait mode on mod switch
            _poseTimer.Stop();
            _useAnimatedAvatar = false;

            ImgAvatar.Visibility = Visibility.Collapsed;
            if (ImgAvatarB != null) ImgAvatarB.Visibility = Visibility.Collapsed;

            ApplyAvatarTransform(_currentAvatarSet); // undo portrait chrome's AvatarBorder shift
            ApplyTubeLayoutOffsets();                // restore the emote set's layout delta

            // If the setup repointed or cleared the active animated layer (legacy animated branch /
            // portrait + static branches null its source), bring the rotation back with a fresh idle.
            // Deferred to a Background tick for the same render-thread-deadlock reason as in
            // EnterCirceEmoteMode. Otherwise the current clip is untouched — just ensure it's shown.
            var active = _circeActiveImg ?? ImgAvatarAnimated;
            var expected = _circeCurrentClip != null ? CirceClipUri(_circeCurrentClip).ToString() : null;
            var actual = AnimationBehavior.GetSourceUri(active)?.ToString();
            if (expected == null || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                StopTalkSequence();
                _circeQueue.Clear();
                _circeReacting = false;
                _circeCurrentClip = null;
                var idle = PickWeightedIdle();
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (_circeEmoteMode && _circeCurrentClip == null) DoCirceCrossfade(idle);
                }));
            }
            else
            {
                active.Visibility = Visibility.Visible;
            }
        }

        // ---- Effective layout: the mod's global TubeLayout + the engaged emote-set's optional delta ----
        // offsetY/detachedY are stored as "+ = down"; the margin math uses "+bottom = up", so they subtract.
        private bool EmoteLayoutActive => _circeEmoteMode && _emoteHasLayout;
        internal double EffAvatarScale() => (App.Mods?.GetAvatarScale() ?? 1.0) * (EmoteLayoutActive ? _emoteScaleMul : 1.0);
        internal int EffAvatarOffsetX() => (App.Mods?.GetAvatarOffsetX() ?? 0) + (EmoteLayoutActive ? _emoteOffX : 0);
        internal int EffAvatarOffsetY() => (App.Mods?.GetAvatarOffsetY() ?? 0) - (EmoteLayoutActive ? _emoteOffY : 0);
        internal int EffAvatarDetachedOffsetX() => (App.Mods?.GetAvatarDetachedOffsetX() ?? 0) + (EmoteLayoutActive ? _emoteDetX : 0);
        internal int EffAvatarDetachedOffsetY() => (App.Mods?.GetAvatarDetachedOffsetY() ?? 0) - (EmoteLayoutActive ? _emoteDetY : 0);

        /// <summary>Audio lead-in for the current spoken line: ~0 when an emote set is animating (voice-first —
        /// the talk animation joins the line late instead of leading it), else the caller's flat fallback.
        /// Read by ShowGiggle.</summary>
        internal double EmoteAudioLeadInSeconds(double fallbackSec)
            => _circeEmoteMode ? _circeNextAudioLeadInMs / 1000.0 : fallbackSec;

        private static void ClearGifLayer(Image img)
        {
            try { AnimationBehavior.SetSourceUri(img, null); } catch { /* ignore */ }
            img.Source = null;
            img.Visibility = Visibility.Collapsed;
        }

        private DispatcherTimer CreateWatchdog()
        {
            var t = new DispatcherTimer();
            t.Tick += (_, __) =>
            {
                t.Stop();
                if (!_circeEmoteMode || _circeTalkSeqActive || _circePendingClip != null) return;
                App.Logger?.Information("[EMOTE] watchdog advance (stuck clip={Clip})", _circeCurrentClip);
                AdvanceCirce(); // a completion event was missed — keep the rotation alive
            };
            return t;
        }

        /// <summary>Bark/voiceline hook (called from PlayEmotionForLine when in emote mode).</summary>
        private void CircePlayEmote(string? emotionLineId, string? audioPath, string? text, string? mood)
        {
            if (!_circeEmoteMode) return;
            double durationSec = !string.IsNullOrEmpty(audioPath) ? AudioDurationSec(audioPath)
                                                                  : EstimateDurationSec(text);
            if (durationSec <= 0) durationSec = EstimateDurationSec(text);

            // A giggle / "mmm" / short non-verbal sound isn't talking — flapping a mouth clip looks wrong.
            // Play one expressive emote instead (giggle / sultry / wink…), held for its full length.
            if (IsNonverbal(text, audioPath, durationSec))
            {
                StartReactionOnly(PickExpressive() ?? PickWeightedIdle(), NonverbalLeadInMs);
                return;
            }

            // [talking clip(s) covering the spoken length] then [one reaction emote for the situation].
            // Lines with no situational reaction (e.g. repetitive flash-audio affirmations) get a random
            // expressive emote so they don't all look the same.
            var talk = PlanTalk(durationSec);
            var reaction = ResolveReaction(emotionLineId, mood) ?? PickExpressive() ?? PickWeightedIdle();
            StartTalkSequence(talk, reaction, durationSec);
        }

        /// <summary>
        /// Drive [talk clip(s)] then [reaction] timed to the voiceline (length <paramref name="durationSec"/>):
        /// the voice fires immediately, the first talk clip joins <see cref="_talkStartDelayMs"/> into the
        /// line, each next clip is brought in so its mouth is already open as the previous one's talking ends
        /// (no mouth-closed gap at the join), and the reaction crossfades in <see cref="_talkLeadOutMs"/>
        /// BEFORE the line ends so the mouth stops with the voice instead of outlasting it. Timer-driven
        /// (not AnimationCompleted-driven). The visual join also waits out the current clip's
        /// <see cref="_minClipMs"/> min-hold so reactions/idles aren't cut short.
        /// </summary>
        private void StartTalkSequence(List<string> talk, string? reaction, double durationSec)
        {
            if (talk.Count == 0) talk = new List<string> { "talkA" };
            StopTalkSequence();

            _circeNextAudioLeadInMs = 0;                          // voice first — audio starts right away
            int vms = (int)Math.Round(Math.Max(0, durationSec) * 1000);
            int startDelay = Math.Max(CurrentClipRemainingHold(), _talkStartDelayMs);

            // Line too short to fit a real talk window between the late join and the early bow-out →
            // skip the mouth clips and hold the situational reaction for the whole line instead.
            if (vms - startDelay - _talkLeadOutMs < MinTalkWindowMs)
            {
                StartReactionOnly(reaction ?? PickWeightedIdle(), 0);
                _circeNextAudioLeadInMs = 0;                      // spoken line stays voice-first
                return;
            }

            void Begin()
            {
                if (!_circeEmoteMode) return;
                _circeReacting = true;
                _circeTalkSeqActive = true;
                _talkSchedule.Clear();

                // t0 = now = startDelay into the voice; talking must be gone _talkLeadOutMs before its end.
                long deadline = vms - _talkLeadOutMs - startDelay;

                var t0 = TalkTiming(talk[0]);
                long coveredEnd = t0.end;                          // talking is covered up to here (rel. t0)
                long lastStart = 0;

                for (int i = 1; i < talk.Count; i++)
                {
                    var ti = TalkTiming(talk[i]);
                    // bring talk[i] in so its mouth (start_i) lands at coveredEnd; keep clips from flashing
                    long at = Math.Max(lastStart + _minClipMs, coveredEnd - ti.start);
                    if (at >= deadline) break;                     // line ends before this clip is needed
                    _talkSchedule.Enqueue((at, talk[i], false));
                    lastStart = at;
                    coveredEnd = at + ti.end;
                }

                // Graceful tail: if the planned clips can't reach a very long line, repeat the last once.
                if (coveredEnd + 250 < deadline)
                {
                    long at = Math.Max(lastStart + _minClipMs, coveredEnd - TalkTiming(talk[^1]).start);
                    if (at < deadline) _talkSchedule.Enqueue((at, talk[^1], false));
                }

                _talkSchedule.Enqueue((deadline, reaction ?? PickWeightedIdle(), true));

                _talkSeqStartTick = Environment.TickCount64;
                DoCirceCrossfade(talk[0]);                         // t0 — first talk clip starts now
                _circeTalkTimer ??= CreateTalkTimer();
                ArmTalkTimer();
            }

            DeferStart(Begin, startDelay);
        }

        /// <summary>
        /// Play a single expressive/reaction emote (no talking) for its full length, then return to idle —
        /// used for giggles / "mmm" / short non-verbal sounds. Honors the min-hold of whatever's showing.
        /// </summary>
        private void StartReactionOnly(string clip, int leadInMs)
        {
            StopTalkSequence();
            int defer = CurrentClipRemainingHold();
            _circeNextAudioLeadInMs = Math.Clamp(defer + leadInMs, 0, 3000);

            void Begin()
            {
                if (!_circeEmoteMode) return;
                _circeReacting = true;
                _circeQueue.Clear();
                _circeTalkSeqActive = false;   // plays once -> AnimationCompleted -> idle rotation
                DoCirceCrossfade(clip);
            }

            if (defer > 0) DeferStart(Begin, defer); else Begin();
        }

        /// <summary>ms the current clip still needs to reach <see cref="_minClipMs"/> on screen (0 if none/elapsed).</summary>
        private int CurrentClipRemainingHold()
        {
            if (_circeCurrentClip == null) return 0;
            long elapsed = Environment.TickCount64 - _circeClipStartTick;
            return (int)Math.Clamp(_minClipMs - elapsed, 0, _minClipMs);
        }

        private DispatcherTimer? _circeStartTimer;
        private Action? _circePendingStart;

        /// <summary>Run <paramref name="begin"/> after <paramref name="ms"/> (deferred sequence start).</summary>
        private void DeferStart(Action begin, int ms)
        {
            _circePendingStart = begin;
            _circeStartTimer ??= CreateStartTimer();
            _circeStartTimer.Stop();
            _circeStartTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
            _circeStartTimer.Start();
        }

        private DispatcherTimer CreateStartTimer()
        {
            var t = new DispatcherTimer();
            t.Tick += (_, __) =>
            {
                t.Stop();
                var a = _circePendingStart; _circePendingStart = null;
                if (_circeEmoteMode) a?.Invoke();
            };
            return t;
        }

        /// <summary>A line is a non-verbal sound (giggle/mmm/moan…) or a tiny untexted clip → expressive emote.</summary>
        private bool IsNonverbal(string? text, string? audioPath, double durationSec)
        {
            if (!string.IsNullOrWhiteSpace(text))
                return NonverbalRe.IsMatch(text.Trim());
            // No text: a very short sound (and we actually have audio) reads as a quick vocalization.
            return !string.IsNullOrEmpty(audioPath) && durationSec > 0 && durationSec <= _nonverbalMaxSec;
        }

        // Whole line is only non-verbal tokens: *giggles*, mmm, mhm, hmm, ahh, ooh, haa, moan(s), sigh(s),
        // teehee, gasp(s), purr(s), ohh, uh — possibly repeated/space-separated. NOT "giggle I love being…".
        private static readonly System.Text.RegularExpressions.Regex NonverbalRe =
            new(@"^(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+)(?:[\s,.~!\-]+(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+))*[\s,.~!\-]*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>A random expressive emote (giggle / sultry / wink…), avoiding the one on screen.</summary>
        private string? PickExpressive()
        {
            var pool = _expressiveEmotes.Where(c => _circeKnownClips.Contains(c) && c != _circeCurrentClip).ToList();
            if (pool.Count == 0) pool = _expressiveEmotes.Where(c => _circeKnownClips.Contains(c)).ToList();
            return pool.Count == 0 ? null : pool[_random.Next(pool.Count)];
        }

        private DispatcherTimer CreateTalkTimer()
        {
            var t = new DispatcherTimer();
            t.Tick += (_, __) =>
            {
                t.Stop();
                if (!_circeEmoteMode || !_circeTalkSeqActive || _talkSchedule.Count == 0)
                {
                    _circeTalkSeqActive = false;
                    return;
                }
                var ev = _talkSchedule.Dequeue();
                if (ev.isReaction) _circeTalkSeqActive = false; // reaction/idle plays once -> its completion → idle
                DoCirceCrossfade(ev.clip);
                if (_circeTalkSeqActive) ArmTalkTimer();         // schedule the next talk transition
            };
            return t;
        }

        /// <summary>Set the talk timer for the next scheduled event, measured from the sequence start.</summary>
        private void ArmTalkTimer()
        {
            if (_talkSchedule.Count == 0) return;
            long elapsed = Environment.TickCount64 - _talkSeqStartTick;
            long wait = Math.Max(1, _talkSchedule.Peek().atMs - elapsed);
            _circeTalkTimer ??= CreateTalkTimer();
            _circeTalkTimer.Stop();
            _circeTalkTimer.Interval = TimeSpan.FromMilliseconds(wait);
            _circeTalkTimer.Start();
        }

        private void StopTalkSequence()
        {
            _circeTalkTimer?.Stop();
            _circeStartTimer?.Stop();
            _circePendingStart = null;
            _circeTalkSeqActive = false;
            _talkSchedule.Clear();
        }

        /// <summary>Speaking window (ms) for a talk clip from clip_timing.json, or a sane fallback.</summary>
        private (int start, int end, int dur) TalkTiming(string clip)
        {
            if (_circeTiming.TryGetValue(clip, out var t))
            {
                int dur = t.durMs > 0 ? t.durMs : TalkFallbackLenMs;
                int start = Math.Clamp(t.startMs, 0, Math.Max(0, dur - 1));
                int end = t.endMs > start ? Math.Min(t.endMs, dur) : dur;
                return (start, end, dur);
            }
            return (TalkFallbackStartMs, TalkFallbackStartMs + TalkFallbackLenMs,
                    TalkFallbackStartMs + TalkFallbackLenMs);
        }

        private int TalkLenMs(string clip) { var t = TalkTiming(clip); return t.end - t.start; }

        /// <summary>
        /// Avatar clicked: rotate to one of the rare affectionate emotes (shy / sultry / adoring / …),
        /// once per <see cref="CirceClickCooldownMs"/>. No-op (returns false) when not in emote mode or
        /// still cooling down, so the caller's other click behavior is unaffected.
        /// </summary>
        internal bool CirceClickEmote()
        {
            if (!_circeEmoteMode) return false;
            long now = Environment.TickCount64;
            if (now - _circeClickCooldownTick < CirceClickCooldownMs) return false;

            var pool = _circeClickEmotes.Where(c => _circeKnownClips.Contains(c) && c != _circeCurrentClip).ToList();
            if (pool.Count == 0) pool = _circeClickEmotes.Where(c => _circeKnownClips.Contains(c)).ToList();
            if (pool.Count == 0) return false;

            _circeClickCooldownTick = now;
            StopTalkSequence();                               // a click interrupts any in-flight spoken line
            _circeReacting = true;
            _circeQueue.Clear();
            CirceCrossfadeTo(pool[_random.Next(pool.Count)]); // plays once, then AdvanceCirce returns to idle
            return true;
        }

        /// <summary>Reaction emote for a line: stemPrefix(id) -> mood(first token) -> none (talk only).</summary>
        private string? ResolveReaction(string? emotionLineId, string? mood)
        {
            if (!string.IsNullOrEmpty(emotionLineId))
            {
                var stem = emotionLineId!.ToLowerInvariant();
                foreach (var kv in _circeStemPrefix)
                    if (stem.StartsWith(kv.Key, StringComparison.Ordinal) && _circeKnownClips.Contains(kv.Value))
                        return kv.Value;
            }
            if (!string.IsNullOrWhiteSpace(mood))
            {
                var token = mood!.Split(',')[0].Trim();
                if (_circeMoodMap.TryGetValue(token, out var clip) && _circeKnownClips.Contains(clip))
                    return clip;
            }
            return null;
        }

        // talk[0] prefers a clip whose mouth opens within this many ms, so a line starts promptly instead
        // of after a long mouth-closed intro. Long-intro clips are used only as continuation clips, where
        // their lead-in is masked by the crossfade from the previous clip.
        private const int TalkFastStartMs = 700;

        /// <summary>
        /// Pick talk clips whose summed speaking-windows cover the voiceline length (fewest clips, capped at
        /// <see cref="_maxTalkClips"/>). The FIRST clip is a quick-opening one (random among the snappy clips
        /// for variety, else the snappiest available); the rest are random for variety. No speed-stretch —
        /// coverage is clips-only, and the runtime cuts the final clip at the line's end.
        /// </summary>
        private List<string> PlanTalk(double durationSec)
        {
            var pool = _talkPool.Where(c => _circeKnownClips.Contains(c)).ToList();
            if (pool.Count == 0) return new List<string> { "talkA" };
            int vms = (int)Math.Round(Math.Max(0, durationSec) * 1000);

            var snappy = pool.Where(c => TalkTiming(c).start <= TalkFastStartMs).ToList();
            string first = snappy.Count > 0
                ? snappy[_random.Next(snappy.Count)]
                : pool.OrderBy(c => TalkTiming(c).start).First();

            var picks = new List<string> { first };
            int covered = TalkLenMs(first);

            var rest = pool.Where(c => c != first).ToList();
            for (int i = rest.Count - 1; i > 0; i--) // shuffle the continuation order
            {
                int j = _random.Next(i + 1);
                (rest[i], rest[j]) = (rest[j], rest[i]);
            }
            foreach (var c in rest)
            {
                if (covered >= vms || picks.Count >= Math.Max(1, _maxTalkClips)) break;
                picks.Add(c);
                covered += TalkLenMs(c);
            }
            return picks;
        }

        /// <summary>One clip finished playing → move to the next (queued reaction, else idle rotation).</summary>
        private void OnCirceClipCompleted(object? sender, AnimationCompletedEventArgs e)
        {
            if (!_circeEmoteMode) return;
            if (_circeTalkSeqActive) return;                       // the talk timer owns transitions mid-line
            if (!ReferenceEquals(sender, _circeActiveImg)) return; // stale outgoing layer
            if (_circePendingClip != null) return;                 // a min-hold swap is already scheduled
            AdvanceCirce();
        }

        /// <summary>
        /// A clip failed to load/render (XamlAnimatedGif raises this instead of throwing — the layer
        /// stays EMPTY and AnimationCompleted never fires, so without intervention the avatar fades
        /// into nothing until the watchdog notices). Log it; if the dead layer is the active one,
        /// nudge the rotation immediately rather than waiting out the full watchdog interval.
        /// </summary>
        private void OnCirceGifError(object? sender, AnimationErrorEventArgs e)
        {
            var layer = ReferenceEquals(sender, ImgAvatarAnimated) ? "A" : "B";
            App.Logger?.Warning("[EMOTE] GIF {Kind} error on layer {Layer} (clip={Clip}): {Error}",
                e.Kind, layer, _circeCurrentClip, e.Exception?.Message);
            if (!_circeEmoteMode || _circeTalkSeqActive) return;
            if (!ReferenceEquals(sender, _circeActiveImg)) return;
            // Recover via the watchdog cadence but shortened: re-arm at 2s so a one-off load failure
            // doesn't leave an empty tube for 13s. (Not advancing synchronously — a persistent failure
            // would otherwise hot-loop through clips.)
            if (_circeWatchdog != null)
            {
                _circeWatchdog.Stop();
                _circeWatchdog.Interval = TimeSpan.FromMilliseconds(2000);
                _circeWatchdog.Start();
            }
        }

        private void AdvanceCirce()
        {
            if (!_circeEmoteMode) return;
            if (_circeQueue.Count > 0) { DoCirceCrossfade(_circeQueue.Dequeue()); return; }
            _circeReacting = false;
            DoCirceCrossfade(PickWeightedIdle());
        }

        /// <summary>
        /// Swap guard: never re-fade to the clip already showing, and hold the current clip at least
        /// <see cref="CirceMinHoldMs"/> before swapping — a sooner request (a new bark interrupting
        /// mid-clip) is coalesced to the latest clip and fired once the minimum elapses. Used for the
        /// first clip of an interrupting line; ordinary rotation goes straight through DoCirceCrossfade.
        /// </summary>
        private void CirceCrossfadeTo(string clip)
        {
            if (!_circeEmoteMode || string.IsNullOrEmpty(clip)) return;
            if (clip == _circeCurrentClip) return; // already on screen; its completion walks the queue

            long elapsed = Environment.TickCount64 - _circeClipStartTick;
            if (_circeCurrentClip != null && elapsed < CirceMinHoldMs)
            {
                _circePendingClip = clip;
                _circeMinHoldTimer ??= CreateMinHoldTimer();
                _circeMinHoldTimer.Stop();
                _circeMinHoldTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, CirceMinHoldMs - elapsed));
                _circeMinHoldTimer.Start();
                return;
            }
            DoCirceCrossfade(clip);
        }

        private DispatcherTimer CreateMinHoldTimer()
        {
            var t = new DispatcherTimer();
            t.Tick += (_, __) =>
            {
                t.Stop();
                if (!_circeEmoteMode) return;
                var c = _circePendingClip; _circePendingClip = null;
                if (!string.IsNullOrEmpty(c) && c != _circeCurrentClip)
                    DoCirceCrossfade(c!);
            };
            return t;
        }

        /// <summary>The actual crossfade between the two animated GIF layers. The clip plays ONCE.</summary>
        private void DoCirceCrossfade(string clip)
        {
            if (!_circeEmoteMode || string.IsNullOrEmpty(clip)) return;

            var outImg = _circeActiveImg ?? ImgAvatarAnimated;
            bool aActive = ReferenceEquals(outImg, ImgAvatarAnimated);
            var inImg = aActive ? ImgAvatarAnimatedB : ImgAvatarAnimated;

            try
            {
                // Force a property CHANGE even if this layer still holds the same URI (its cleanup is
                // skipped when a crossfade is interrupted mid-fade — the replaced fade's Completed never
                // runs ClearGifLayer). Re-setting an identical URI would be a silent no-op: the old,
                // already-finished animator stays (RepeatBehavior(1)) → no restart, no AnimationCompleted,
                // and the rotation strands on a dead layer.
                if (AnimationBehavior.GetSourceUri(inImg) != null)
                    AnimationBehavior.SetSourceUri(inImg, null);
                AnimationBehavior.SetRepeatBehavior(inImg, new RepeatBehavior(1)); // play once, then rotate
                AnimationBehavior.SetAutoStart(inImg, true);
                AnimationBehavior.SetSourceUri(inImg, CirceClipUri(clip));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Emote clip load failed ({Clip}): {Error}", clip, ex.Message);
                // Keep the self-heal alive: without this a failed load left the watchdog unarmed and
                // the rotation permanently dead on whatever was last shown.
                _circeWatchdog ??= CreateWatchdog();
                _circeWatchdog.Stop();
                _circeWatchdog.Interval = TimeSpan.FromMilliseconds(CirceWatchdogMs);
                _circeWatchdog.Start();
                return;
            }
            App.Logger?.Information("[EMOTE] xfade -> {Clip} (in={In}, outOp={OutOp:F2}, talkSeq={Talk})",
                clip, ReferenceEquals(inImg, ImgAvatarAnimated) ? "A" : "B", outImg.Opacity, _circeTalkSeqActive);

            inImg.Opacity = 0;
            inImg.Visibility = Visibility.Visible;

            // Per-clip scale: layout scale × this clip's optional extra multiplier (e.g. talkB shrunk
            // 10% more). Set on the incoming layer so it overrides the uniform base from ApplyTubeLayoutOffsets.
            double effScale = EffAvatarScale() * (_circeClipScale.TryGetValue(clip, out var m) ? m : 1.0);
            inImg.LayoutTransform = Math.Abs(effScale - 1.0) > 0.001
                ? new System.Windows.Media.ScaleTransform(effScale, effScale)
                : null;

            var dur = TimeSpan.FromMilliseconds(_circeFadeMs);
            var fin = new DoubleAnimation(0, 1, dur) { FillBehavior = FillBehavior.Stop };
            var fout = new DoubleAnimation(outImg.Opacity, 0, dur) { FillBehavior = FillBehavior.Stop };
            fin.Completed += (_, __) =>
            {
                inImg.Opacity = 1;
                if (!ReferenceEquals(outImg, inImg))
                {
                    outImg.Opacity = 0;
                    ClearGifLayer(outImg); // stop + free the outgoing clip
                }
            };
            _circeActiveImg = inImg;
            _circeCurrentClip = clip;
            _circeClipStartTick = Environment.TickCount64;

            // Gate the visible crossfade on the incoming clip being LOADED (its animator exists / first
            // frame is decoded). Until then the outgoing layer keeps its current opacity, so the avatar
            // never blanks while the new GIF loads async — the "disappears for a few seconds" bug, where
            // the outgoing faded to 0 over ~1s before the incoming had a frame (and a stranded load left
            // it blank until the 13s watchdog). Bounded by an 800ms timeout so a slow/failed load still
            // proceeds and the existing AnimationCompleted / watchdog recovery takes over. Idle/reaction
            // rotation only: talk sequences switch clips on a tight timer and stay on the immediate path
            // so their mouth-join timing is unchanged.
            void StartFade()
            {
                outImg.BeginAnimation(UIElement.OpacityProperty, fout);
                inImg.BeginAnimation(UIElement.OpacityProperty, fin);
            }
            bool ready = AnimationBehavior.GetAnimator(inImg) != null || inImg.Source != null;
            if (ready || _circeTalkSeqActive)
            {
                StartFade();
            }
            else
            {
                long gateStart = Environment.TickCount64;
                var gate = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                gate.Tick += (_, __) =>
                {
                    // A newer crossfade claimed these layers — abandon this gate, it owns them now.
                    if (!ReferenceEquals(_circeActiveImg, inImg) || _circeCurrentClip != clip) { gate.Stop(); return; }
                    bool nowReady = AnimationBehavior.GetAnimator(inImg) != null || inImg.Source != null;
                    if (nowReady || Environment.TickCount64 - gateStart > 800)
                    {
                        gate.Stop();
                        StartFade();
                    }
                };
                gate.Start();
            }

            // (Re)arm the watchdog so the rotation survives a missed AnimationCompleted.
            _circeWatchdog ??= CreateWatchdog();
            _circeWatchdog.Stop();
            _circeWatchdog.Interval = TimeSpan.FromMilliseconds(CirceWatchdogMs);
            _circeWatchdog.Start();
        }

        private string PickWeightedIdle()
        {
            if (_circeIdle.Count == 0) return "idle";
            var pool = _circeIdle.Where(x => x.clip != _circeCurrentClip).ToList();
            if (pool.Count == 0) pool = _circeIdle;
            int total = pool.Sum(x => Math.Max(1, x.weight));
            int r = _random.Next(total);
            foreach (var (clip, weight) in pool)
            {
                r -= Math.Max(1, weight);
                if (r < 0) return clip;
            }
            return pool[0].clip;
        }

        private Uri CirceClipUri(string clip)
            => new Uri($"pack://application:,,,/Resources/{_emoteFolder}/{clip}.gif", UriKind.Absolute);

        private bool LoadCirceMap()
        {
            if (string.IsNullOrEmpty(_emoteFolder)) return false;
            if (_circeMapValid && string.Equals(_loadedEmoteFolder, _emoteFolder, StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                // Standard map name is emotes.json; fall back to the legacy <folder>.json name.
                JObject? j = ReadMapJson($"Resources/{_emoteFolder}/emotes.json")
                          ?? ReadMapJson($"Resources/{_emoteFolder}/{_emoteFolder}.json");
                if (j == null) { App.Logger?.Warning("emotes.json not found in {Folder}", _emoteFolder); return false; }

                _circeFadeMs = (int?)j["fadeMs"] ?? 1000;

                _circeIdle.Clear();
                if (j["idleRotation"] is JArray arr)
                    foreach (var it in arr)
                        _circeIdle.Add(((string?)it["clip"] ?? "idle", (int?)it["weight"] ?? 1));
                if (_circeIdle.Count == 0) _circeIdle.Add(("idle", 1));

                if (j["talking"] is JObject t)
                {
                    // Preferred: a single random "pool". Back-compat: merge legacy short/medium/long lists.
                    if (t["pool"] != null)
                        _talkPool = ParseClipList(t["pool"], _talkPool);
                    else
                        _talkPool = ParseClipList(t["short"], new List<string>())
                                    .Concat(ParseClipList(t["medium"], new List<string>()))
                                    .Concat(ParseClipList(t["long"], new List<string>()))
                                    .Distinct().DefaultIfEmpty("talkA").ToList();
                    _talkLongMin = (double?)t["longMinSec"] ?? _talkLongMin;
                    _maxTalkClips = Math.Max(1, (int?)t["maxTalkClips"] ?? _maxTalkClips);
                }

                _circeStemPrefix.Clear();
                if (j["stemPrefix"] is JObject sp)
                    foreach (var p in sp.Properties())
                        _circeStemPrefix.Add(new(p.Name.ToLowerInvariant(), (string?)p.Value ?? ""));
                _circeStemPrefix.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length)); // longest prefix first

                _circeMoodMap.Clear();
                if (j["mood"] is JObject mm)
                    foreach (var p in mm.Properties())
                        _circeMoodMap[p.Name] = (string?)p.Value ?? "";

                _circeClipScale.Clear();
                if (j["clipScale"] is JObject csm)
                    foreach (var p in csm.Properties())
                        if (p.Value.Type == JTokenType.Float || p.Value.Type == JTokenType.Integer)
                            _circeClipScale[p.Name] = (double)p.Value;

                _circeClickEmotes.Clear();
                if (j["clickEmotes"] is JArray ce)
                    foreach (var it in ce)
                    {
                        var s = (string?)it;
                        if (!string.IsNullOrEmpty(s)) _circeClickEmotes.Add(s!);
                    }
                if (_circeClickEmotes.Count == 0)
                    _circeClickEmotes.AddRange(new[] { "shy", "sultry", "adoring", "tender", "blowkiss" });

                // Pacing knobs + the expressive pool (giggle/mmm + reaction-less lines).
                _talkStartDelayMs = Math.Max(0, (int?)j["talkStartDelayMs"] ?? _talkStartDelayMs);
                _talkLeadOutMs = Math.Max(0, (int?)j["talkLeadOutMs"] ?? _talkLeadOutMs);
                _minClipMs = Math.Max(0, (int?)j["minClipMs"] ?? _minClipMs);
                if (j["expressive"] is JObject ex)
                {
                    _expressiveEmotes = ParseClipList(ex["pool"], _expressiveEmotes);
                    _nonverbalMaxSec = (double?)ex["nonverbalMaxSec"] ?? _nonverbalMaxSec;
                }

                // Per-clip speaking windows (clip_timing.json, written by the trim tool). Missing -> fallback.
                _circeTiming.Clear();
                var tj = ReadMapJson($"Resources/{_emoteFolder}/clip_timing.json");
                if (tj != null)
                    foreach (var p in tj.Properties())
                        if (p.Value is JObject o && !p.Name.StartsWith("_", StringComparison.Ordinal))
                            _circeTiming[p.Name] = ((int?)o["speakStartMs"] ?? 0,
                                                    (int?)o["speakEndMs"] ?? 0,
                                                    (int?)o["durationMs"] ?? 0);

                // Known-clip set = every clip name the map references, so typos/missing clips are rejected
                // without a hardcoded whitelist (each pose may ship a different set).
                _circeKnownClips.Clear();
                foreach (var (clip, _) in _circeIdle) _circeKnownClips.Add(clip);
                foreach (var c in _talkPool) _circeKnownClips.Add(c);
                foreach (var kv in _circeStemPrefix) if (!string.IsNullOrEmpty(kv.Value)) _circeKnownClips.Add(kv.Value);
                foreach (var v in _circeMoodMap.Values) if (!string.IsNullOrEmpty(v)) _circeKnownClips.Add(v);
                foreach (var c in _circeClickEmotes) _circeKnownClips.Add(c);
                foreach (var c in _expressiveEmotes) _circeKnownClips.Add(c);

                // Optional per-set layout override.
                _emoteHasLayout = false;
                if (j["layout"] is JObject ly)
                {
                    _emoteScaleMul = (double?)ly["scale"] ?? 1.0; // multiplier, 0.9 = 10% smaller
                    _emoteOffX = (int?)ly["offsetX"] ?? 0;        // + = right
                    _emoteOffY = (int?)ly["offsetY"] ?? 0;        // + = down
                    _emoteDetX = (int?)ly["detachedX"] ?? 0;      // + = right (detached)
                    _emoteDetY = (int?)ly["detachedY"] ?? 0;      // + = down  (detached)
                    _emoteHasLayout = true;
                }

                _loadedEmoteFolder = _emoteFolder;
                _circeMapValid = true;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to load emotes.json for {Folder}", _emoteFolder);
                _circeMapValid = false;
                return false;
            }
        }

        private static List<string> ParseClipList(JToken? t, List<string> fallback)
        {
            if (t == null) return fallback;
            if (t.Type == JTokenType.Array)
            {
                var l = t.Select(x => (string?)x).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
                return l.Count > 0 ? l : fallback;
            }
            var single = (string?)t;
            return string.IsNullOrEmpty(single) ? fallback : new List<string> { single! };
        }

        private bool _circeMapValid;

        private static JObject? ReadMapJson(string relativePackPath)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/{relativePackPath}", UriKind.Absolute);
                var sri = Application.GetResourceStream(uri);
                if (sri == null) return null;
                using var r = new System.IO.StreamReader(sri.Stream);
                return JObject.Parse(r.ReadToEnd());
            }
            catch { return null; }
        }

        /// <summary>Pause GIF playback + rotation when the avatar is offscreen (CPU saving).</summary>
        private void CircePause()
        {
            if (!_circeEmoteMode) return;
            try { AnimationBehavior.GetAnimator(ImgAvatarAnimated)?.Pause(); } catch { }
            try { AnimationBehavior.GetAnimator(ImgAvatarAnimatedB)?.Pause(); } catch { }
            StopTalkSequence(); // abort any in-flight spoken line; resume returns to idle rotation
            _circeWatchdog?.Stop(); _circeMinHoldTimer?.Stop();
        }

        private void CirceResume()
        {
            if (!_circeEmoteMode) return;
            try { AnimationBehavior.GetAnimator(ImgAvatarAnimated)?.Play(); } catch { }
            try { AnimationBehavior.GetAnimator(ImgAvatarAnimatedB)?.Play(); } catch { }
            // Re-arm the watchdog so rotation keeps going after returning on-screen.
            if (_circeCurrentClip != null && _circeWatchdog != null)
            {
                _circeWatchdog.Stop();
                _circeWatchdog.Interval = TimeSpan.FromMilliseconds(CirceWatchdogMs);
                _circeWatchdog.Start();
            }
        }
    }
}
