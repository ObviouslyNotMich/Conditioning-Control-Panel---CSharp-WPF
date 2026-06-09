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
    /// Data-driven animated emote layer for built-in companion poses (Circe first). When the active
    /// mod + current avatar set match an entry in <c>Resources/avatar_emotes_registry.json</c>, the
    /// static/portrait avatar is replaced by transparent looping GIF clips from that entry's folder:
    /// an idle that alternates (idle4 / idle5 / phone) and reaction/talking clips driven by the bark
    /// the companion speaks. Swaps fast-crossfade between two animated layers (XamlAnimatedGif, which
    /// handles GIF looping + transparency natively).
    ///
    /// To add a pose/mod: produce the clips (see tools/avatar-emotes/README.md), drop them in a new
    /// Resources/&lt;folder&gt;/ with an emotes.json, add one line to the registry, and embed both in the
    /// csproj. No code changes here. The class name is historical ("Circe"); the logic is generic.
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private bool _circeEmoteMode;
        private bool _circeReacting;
        private string? _circeCurrentClip;
        private Image? _circeActiveImg; // ImgAvatarAnimated or ImgAvatarAnimatedB

        // The resource folder backing the currently-engaged emote set (e.g. "circe_emotes"), and the
        // folder whose emotes.json is currently parsed into the fields below (so a pose switch reloads).
        private string? _emoteFolder;
        private string? _loadedEmoteFolder;

        private DispatcherTimer? _circeIdleTimer;
        private DispatcherTimer? _circeReturnTimer;

        // Minimum on-screen time per clip: a swap requested sooner is coalesced and deferred,
        // so no clip ever flashes by faster than this (rapid back-to-back bark lines).
        private const int CirceMinHoldMs = 2000;
        private long _circeClipStartTick;
        private string? _circePendingClip;
        private bool _circePendingStartRotation;
        private DispatcherTimer? _circeMinHoldTimer;

        // mapping (from Resources/<folder>/emotes.json)
        private int _circeFadeMs = 800;   // crossfade dissolve between poses (emotes.json "fadeMs" overrides)
        private double _circeIdleSwapSec = 9;
        private readonly List<(string clip, int weight)> _circeIdle = new();
        private readonly List<KeyValuePair<string, string>> _circeStemPrefix = new(); // longest-first
        private readonly Dictionary<string, string> _circeMoodMap = new(StringComparer.OrdinalIgnoreCase);
        private string _talkShort = "idle", _talkMed = "talkA", _talkLong = "idle6";
        private double _talkShortMax = 3.0, _talkLongMin = 7.0;
        // Clip names referenced by the loaded map (idles + talking + overrides + moods). Used to reject
        // typos/missing mappings; built per-folder so each pose may ship a different clip set.
        private readonly HashSet<string> _circeKnownClips = new(StringComparer.OrdinalIgnoreCase);

        // Optional per-emote-set layout override (emotes.json "layout"). When present and engaged, these
        // win over the mod's global TubeLayout so a pose can be sized/placed independently of the static
        // avatar. Absent -> fall back to App.Mods (current pose-1 behavior, unchanged).
        private bool _emoteHasLayout;
        private double _emoteScale = 1.0;
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

            ImgAvatar.Visibility = Visibility.Collapsed;
            if (ImgAvatarB != null) ImgAvatarB.Visibility = Visibility.Collapsed;

            ClearGifLayer(ImgAvatarAnimated);
            ClearGifLayer(ImgAvatarAnimatedB);
            ImgAvatarAnimated.Opacity = 1;
            ImgAvatarAnimatedB.Opacity = 0;
            _circeActiveImg = ImgAvatarAnimated;

            _circeReacting = false;
            _circeEmoteMode = true;
            _circeReturnTimer ??= CreateReturnTimer();
            _circeIdleTimer ??= CreateIdleTimer();

            ApplyTubeLayoutOffsets();     // pick up this set's optional layout override
            CirceCrossfadeTo(PickWeightedIdle(), startIdleRotation: true);
            App.Logger?.Information("Emote mode engaged ({Folder}, set {Set}).", folder, _currentAvatarSet);
        }

        private void LeaveCirceEmoteMode()
        {
            _circeEmoteMode = false;
            _circeReacting = false;
            _emoteFolder = null;
            _emoteHasLayout = false;
            _circeIdleTimer?.Stop();
            _circeReturnTimer?.Stop();
            _circeMinHoldTimer?.Stop();
            _circePendingClip = null;
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

        // ---- Effective layout (emote-set override wins over the mod's global TubeLayout) ----
        // ApplyTubeLayoutOffsets and the speech-bubble positioning route through these so an engaged
        // pose with a "layout" block sizes/places itself independently of the static avatar.
        internal double EffAvatarScale() => (_circeEmoteMode && _emoteHasLayout) ? _emoteScale : (App.Mods?.GetAvatarScale() ?? 1.0);
        internal int EffAvatarOffsetX() => (_circeEmoteMode && _emoteHasLayout) ? _emoteOffX : (App.Mods?.GetAvatarOffsetX() ?? 0);
        internal int EffAvatarOffsetY() => (_circeEmoteMode && _emoteHasLayout) ? _emoteOffY : (App.Mods?.GetAvatarOffsetY() ?? 0);
        internal int EffAvatarDetachedOffsetX() => (_circeEmoteMode && _emoteHasLayout) ? _emoteDetX : (App.Mods?.GetAvatarDetachedOffsetX() ?? 0);
        internal int EffAvatarDetachedOffsetY() => (_circeEmoteMode && _emoteHasLayout) ? _emoteDetY : (App.Mods?.GetAvatarDetachedOffsetY() ?? 0);

        private static void ClearGifLayer(Image img)
        {
            try { AnimationBehavior.SetSourceUri(img, null); } catch { /* ignore */ }
            img.Source = null;
            img.Visibility = Visibility.Collapsed;
        }

        private DispatcherTimer CreateIdleTimer()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_circeIdleSwapSec) };
            t.Tick += (_, __) =>
            {
                if (!_circeEmoteMode || _circeReacting) return;
                CirceCrossfadeTo(PickWeightedIdle());
            };
            return t;
        }

        private DispatcherTimer CreateReturnTimer()
        {
            var t = new DispatcherTimer();
            t.Tick += (_, __) =>
            {
                t.Stop();
                if (!_circeEmoteMode) return;
                _circeReacting = false;
                CirceCrossfadeTo(PickWeightedIdle(), startIdleRotation: true);
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
            string clip = ResolveCirceClip(emotionLineId, mood, durationSec);

            _circeReacting = true;
            _circeIdleTimer?.Stop();
            CirceCrossfadeTo(clip);

            double hold = Math.Max(durationSec, 2.0) + _circeFadeMs / 1000.0;
            _circeReturnTimer ??= CreateReturnTimer();
            _circeReturnTimer.Stop();
            _circeReturnTimer.Interval = TimeSpan.FromSeconds(hold);
            _circeReturnTimer.Start();
        }

        private string ResolveCirceClip(string? emotionLineId, string? mood, double durationSec)
        {
            // 1) stem-prefix override (e.g. attention_lost* -> disappointed2)
            if (!string.IsNullOrEmpty(emotionLineId))
            {
                var stem = emotionLineId!.ToLowerInvariant();
                foreach (var kv in _circeStemPrefix)
                    if (stem.StartsWith(kv.Key, StringComparison.Ordinal) && _circeKnownClips.Contains(kv.Value))
                        return kv.Value;
            }
            // 2) mood (first comma token) -> emote
            if (!string.IsNullOrWhiteSpace(mood))
            {
                var token = mood!.Split(',')[0].Trim();
                if (_circeMoodMap.TryGetValue(token, out var clip) && _circeKnownClips.Contains(clip))
                    return clip;
            }
            // 3) talking, sized by spoken length
            if (durationSec > 0 && durationSec < _talkShortMax) return _talkShort;
            if (durationSec >= _talkLongMin) return _talkLong;
            return _talkMed;
        }

        /// <summary>
        /// Crossfade guard: never re-fade to the clip already showing (consecutive same-clip lines),
        /// and hold the current clip at least <see cref="CirceMinHoldMs"/> before swapping — a sooner
        /// request is coalesced to the latest clip and fired once the minimum elapses.
        /// </summary>
        private void CirceCrossfadeTo(string clip, bool startIdleRotation = false)
        {
            if (!_circeEmoteMode || string.IsNullOrEmpty(clip)) return;

            if (clip == _circeCurrentClip)
            {
                // Same clip already on screen — keep it, just (re)arm idle rotation if asked.
                if (startIdleRotation && _circeIdleTimer != null)
                {
                    _circeIdleTimer.Stop();
                    _circeIdleTimer.Interval = TimeSpan.FromSeconds(_circeIdleSwapSec);
                    _circeIdleTimer.Start();
                }
                return;
            }

            long elapsed = Environment.TickCount64 - _circeClipStartTick;
            if (_circeCurrentClip != null && elapsed < CirceMinHoldMs)
            {
                _circePendingClip = clip;
                _circePendingStartRotation = startIdleRotation;
                _circeMinHoldTimer ??= CreateMinHoldTimer();
                _circeMinHoldTimer.Stop();
                _circeMinHoldTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, CirceMinHoldMs - elapsed));
                _circeMinHoldTimer.Start();
                return;
            }

            DoCirceCrossfade(clip, startIdleRotation);
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
                    DoCirceCrossfade(c!, _circePendingStartRotation);
            };
            return t;
        }

        /// <summary>The actual fast crossfade between the two animated GIF layers.</summary>
        private void DoCirceCrossfade(string clip, bool startIdleRotation)
        {
            var outImg = _circeActiveImg ?? ImgAvatarAnimated;
            bool aActive = ReferenceEquals(outImg, ImgAvatarAnimated);
            var inImg = aActive ? ImgAvatarAnimatedB : ImgAvatarAnimated;

            try
            {
                AnimationBehavior.SetRepeatBehavior(inImg, RepeatBehavior.Forever);
                AnimationBehavior.SetAutoStart(inImg, true);
                AnimationBehavior.SetSourceUri(inImg, CirceClipUri(clip));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Emote clip load failed ({Clip}): {Error}", clip, ex.Message);
                return;
            }

            inImg.Opacity = 0;
            inImg.Visibility = Visibility.Visible;

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
            outImg.BeginAnimation(UIElement.OpacityProperty, fout);
            inImg.BeginAnimation(UIElement.OpacityProperty, fin);

            _circeActiveImg = inImg;
            _circeCurrentClip = clip;
            _circeClipStartTick = Environment.TickCount64;

            if (startIdleRotation && _circeIdleTimer != null)
            {
                _circeIdleTimer.Stop();
                _circeIdleTimer.Interval = TimeSpan.FromSeconds(_circeIdleSwapSec);
                _circeIdleTimer.Start();
            }
        }

        private string PickWeightedIdle()
        {
            if (_circeIdle.Count == 0) return "idle4";
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

                _circeFadeMs = (int?)j["fadeMs"] ?? 800;
                _circeIdleSwapSec = (double?)j["idleSwapSeconds"] ?? 9;

                _circeIdle.Clear();
                if (j["idleRotation"] is JArray arr)
                    foreach (var it in arr)
                        _circeIdle.Add(((string?)it["clip"] ?? "idle4", (int?)it["weight"] ?? 1));
                if (_circeIdle.Count == 0) _circeIdle.Add(("idle4", 1));

                if (j["talking"] is JObject t)
                {
                    _talkShort = (string?)t["short"] ?? _talkShort;
                    _talkMed = (string?)t["medium"] ?? _talkMed;
                    _talkLong = (string?)t["long"] ?? _talkLong;
                    _talkShortMax = (double?)t["shortMaxSec"] ?? _talkShortMax;
                    _talkLongMin = (double?)t["longMinSec"] ?? _talkLongMin;
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

                // Known-clip set = every clip name the map references, so typos/missing clips are rejected
                // without a hardcoded whitelist (each pose may ship a different set).
                _circeKnownClips.Clear();
                foreach (var (clip, _) in _circeIdle) _circeKnownClips.Add(clip);
                _circeKnownClips.Add(_talkShort); _circeKnownClips.Add(_talkMed); _circeKnownClips.Add(_talkLong);
                foreach (var kv in _circeStemPrefix) if (!string.IsNullOrEmpty(kv.Value)) _circeKnownClips.Add(kv.Value);
                foreach (var v in _circeMoodMap.Values) if (!string.IsNullOrEmpty(v)) _circeKnownClips.Add(v);

                // Optional per-set layout override.
                _emoteHasLayout = false;
                if (j["layout"] is JObject ly)
                {
                    _emoteScale = (double?)ly["scale"] ?? 1.0;
                    _emoteOffX = (int?)ly["offsetX"] ?? 0;
                    _emoteOffY = (int?)ly["offsetY"] ?? 0;
                    _emoteDetX = (int?)ly["detachedX"] ?? 0;
                    _emoteDetY = (int?)ly["detachedY"] ?? 0;
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
            _circeIdleTimer?.Stop(); _circeReturnTimer?.Stop(); _circeMinHoldTimer?.Stop();
        }

        private void CirceResume()
        {
            if (!_circeEmoteMode) return;
            try { AnimationBehavior.GetAnimator(ImgAvatarAnimated)?.Play(); } catch { }
            try { AnimationBehavior.GetAnimator(ImgAvatarAnimatedB)?.Play(); } catch { }
            if (!_circeReacting) _circeIdleTimer?.Start();
        }
    }
}
