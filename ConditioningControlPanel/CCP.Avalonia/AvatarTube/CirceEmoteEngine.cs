using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.AvatarTube;

/// <summary>
/// Data-driven animated emote engine for the Avalonia AvatarTube.
/// Mirrors the WPF Circe emote behaviour using two image layers, the
/// AvaloniaAnimatedGif renderer, and JSON maps under Assets/&lt;folder&gt;/.
/// </summary>
internal sealed class CirceEmoteEngine : IDisposable
{
    private readonly Image _layerA;
    private readonly Image _layerB;
    private readonly IAssetLoader _assetLoader;
    private readonly IAppLogger? _logger;
    private readonly Random _random;

    private AvaloniaAnimatedGif? _playerA;
    private AvaloniaAnimatedGif? _playerB;
    private Image _activeImg;
    private Image _inactiveImg;

    private bool _isActive;
    private string? _folder;
    private string? _currentClip;
    private readonly Queue<string> _queue = new();
    private bool _talkSeqActive;
    private readonly Queue<(long atMs, string clip, bool isReaction)> _talkSchedule = new();
    private long _talkSeqStartTick;

    private readonly DispatcherTimer _watchdog = new();
    private readonly DispatcherTimer _talkTimer = new();
    private readonly DispatcherTimer _startTimer = new();
    private readonly DispatcherTimer _minHoldTimer = new();
    private Action? _pendingStart;
    private string? _pendingClip;
    private long _clipStartTick;

    private readonly List<(string clip, int weight)> _idle = new();
    private readonly List<KeyValuePair<string, string>> _stemPrefix = new();
    private readonly Dictionary<string, string> _moodMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _clipScale = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _talkPool = new() { "talkA", "talkC" };
    private List<string> _expressive = new() { "giggle", "sultry", "wink", "tease", "blowkiss", "adoring", "flirt", "shy" };
    private readonly List<string> _clickEmotes = new() { "shy", "sultry", "adoring", "tender", "blowkiss" };
    private readonly HashSet<string> _knownClips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int startMs, int endMs, int durMs)> _timing = new(StringComparer.OrdinalIgnoreCase);

    private int _fadeMs = 1000;
    private int _talkStartDelayMs = 1000;
    private int _talkLeadOutMs = 500;
    private int _minClipMs = 2500;
    private int _maxTalkClips = 3;
    private double _nonverbalMaxSec = 1.4;
    private long _clickCooldownTick = long.MinValue;

    private bool _emoteHasLayout;
    private double _emoteScaleMul = 1.0;
    private int _emoteOffX, _emoteOffY, _emoteDetX, _emoteDetY;

    private OpacityFade? _fadeOut;
    private OpacityFade? _fadeIn;

    private const int PollMs = 500;
    private const int MinHoldMs = 2000;
    private const int ClickCooldownMs = 3000;
    private const int TalkFastStartMs = 700;
    private const int TalkFallbackStartMs = 600;
    private const int TalkFallbackLenMs = 2500;
    private const int MinTalkWindowMs = 500;
    private const int NonverbalLeadInMs = 250;

    public CirceEmoteEngine(Image layerA, Image layerB, IAssetLoader assetLoader, IAppLogger? logger, Random random)
    {
        _layerA = layerA;
        _layerB = layerB;
        _activeImg = layerA;
        _inactiveImg = layerB;
        _assetLoader = assetLoader;
        _logger = logger;
        _random = random;

        _watchdog.Interval = TimeSpan.FromMilliseconds(PollMs);
        _watchdog.Tick += (_, _) => OnWatchdogTick();

        _talkTimer.Tick += (_, _) => OnTalkTimerTick();
        _startTimer.Tick += (_, _) => OnStartTimerTick();
        _minHoldTimer.Tick += (_, _) => OnMinHoldTimerTick();
    }

    public bool IsActive => _isActive;
    public string? CurrentClip => _currentClip;
    public int AudioLeadInMs { get; private set; }

    public bool HasLayout => _emoteHasLayout;
    public double EffScaleMul => _emoteScaleMul;
    public int EffOffsetX => _emoteOffX;
    public int EffOffsetY => _emoteOffY;
    public int EffDetachedOffsetX => _emoteDetX;
    public int EffDetachedOffsetY => _emoteDetY;

    public event Action<string>? ClipStarted;

    public async Task<bool> EnterAsync(string folder)
    {
        if (string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase) && _isActive)
            return true;

        Leave();
        _folder = folder;

        if (!await LoadMapAsync(folder))
        {
            _folder = null;
            return false;
        }

        _isActive = true;
        _layerA.IsVisible = true;
        _layerB.IsVisible = true;
        _layerA.Opacity = 1;
        _layerB.Opacity = 0;
        await CrossfadeToAsync(PickWeightedIdle());
        _watchdog.Start();
        return true;
    }

    public void Leave()
    {
        _isActive = false;
        _talkSeqActive = false;
        _talkSchedule.Clear();
        _queue.Clear();
        _watchdog.Stop();
        _talkTimer.Stop();
        _startTimer.Stop();
        _minHoldTimer.Stop();
        _pendingStart = null;
        _pendingClip = null;
        _fadeOut?.Cancel();
        _fadeIn?.Cancel();
        _fadeOut = null;
        _fadeIn = null;
        DisposePlayer(ref _playerA);
        DisposePlayer(ref _playerB);
        _layerA.Source = null;
        _layerB.Source = null;
        _layerA.IsVisible = false;
        _layerB.IsVisible = false;
        _currentClip = null;
        _emoteHasLayout = false;
    }

    public void Pause()
    {
        _watchdog.Stop();
        _talkTimer.Stop();
        _startTimer.Stop();
        _playerA?.Stop();
        _playerB?.Stop();
    }

    public void Resume()
    {
        if (!_isActive) return;
        _playerA?.Start();
        _playerB?.Start();
        if (_currentClip != null)
            _watchdog.Start();
    }

    public void PlayEmote(string? emotionLineId, string? audioPath, string? text, string? mood)
    {
        if (!_isActive) return;

        double durationSec = !string.IsNullOrEmpty(audioPath) ? EstimateAudioDurationSec(audioPath)
                                                              : EstimateDurationSec(text);
        if (durationSec <= 0) durationSec = EstimateDurationSec(text);

        if (IsNonverbal(text, audioPath, durationSec))
        {
            StartReactionOnly(PickExpressive() ?? PickWeightedIdle(), NonverbalLeadInMs);
            return;
        }

        var talk = PlanTalk(durationSec);
        var reaction = ResolveReaction(emotionLineId, mood) ?? PickExpressive() ?? PickWeightedIdle();
        StartTalkSequence(talk, reaction, durationSec);
    }

    public bool ClickEmote()
    {
        if (!_isActive) return false;
        long now = Environment.TickCount64;
        if (now - _clickCooldownTick < ClickCooldownMs) return false;

        var pool = _clickEmotes.Where(c => _knownClips.Contains(c) && c != _currentClip).ToList();
        if (pool.Count == 0) pool = _clickEmotes.Where(c => _knownClips.Contains(c)).ToList();
        if (pool.Count == 0) return false;

        _clickCooldownTick = now;
        StopTalkSequence();
        _queue.Clear();
        CrossfadeTo(pool[_random.Next(pool.Count)]);
        return true;
    }

    private async Task<bool> LoadMapAsync(string folder)
    {
        try
        {
            var emotesJson = await _assetLoader.ReadTextAsync(AssetUri($"Assets/{folder}/emotes.json"));
            var j = JObject.Parse(emotesJson);

            _fadeMs = (int?)j["fadeMs"] ?? 1000;

            _idle.Clear();
            if (j["idleRotation"] is JArray idleArr)
                foreach (var it in idleArr)
                    _idle.Add(((string?)it["clip"] ?? "idle", (int?)it["weight"] ?? 1));
            if (_idle.Count == 0) _idle.Add(("idle", 1));

            if (j["talking"] is JObject t)
            {
                if (t["pool"] != null)
                    _talkPool = ParseClipList(t["pool"], _talkPool);
                else
                    _talkPool = ParseClipList(t["short"], new List<string>())
                        .Concat(ParseClipList(t["medium"], new List<string>()))
                        .Concat(ParseClipList(t["long"], new List<string>()))
                        .Distinct().DefaultIfEmpty("talkA").ToList();
                _maxTalkClips = Math.Max(1, (int?)t["maxTalkClips"] ?? _maxTalkClips);
            }

            _stemPrefix.Clear();
            if (j["stemPrefix"] is JObject sp)
                foreach (var p in sp.Properties())
                    _stemPrefix.Add(new(p.Name.ToLowerInvariant(), (string?)p.Value ?? ""));
            _stemPrefix.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

            _moodMap.Clear();
            if (j["mood"] is JObject mm)
                foreach (var p in mm.Properties())
                    _moodMap[p.Name] = (string?)p.Value ?? "";

            _clipScale.Clear();
            if (j["clipScale"] is JObject csm)
                foreach (var p in csm.Properties())
                    if (p.Value.Type == JTokenType.Float || p.Value.Type == JTokenType.Integer)
                        _clipScale[p.Name] = (double)p.Value;

            _clickEmotes.Clear();
            if (j["clickEmotes"] is JArray ce)
                foreach (var it in ce)
                {
                    var s = (string?)it;
                    if (!string.IsNullOrEmpty(s)) _clickEmotes.Add(s!);
                }
            if (_clickEmotes.Count == 0)
                _clickEmotes.AddRange(new[] { "shy", "sultry", "adoring", "tender", "blowkiss" });

            _talkStartDelayMs = Math.Max(0, (int?)j["talkStartDelayMs"] ?? _talkStartDelayMs);
            _talkLeadOutMs = Math.Max(0, (int?)j["talkLeadOutMs"] ?? _talkLeadOutMs);
            _minClipMs = Math.Max(0, (int?)j["minClipMs"] ?? _minClipMs);
            if (j["expressive"] is JObject ex)
            {
                _expressive = ParseClipList(ex["pool"], _expressive);
                _nonverbalMaxSec = (double?)ex["nonverbalMaxSec"] ?? _nonverbalMaxSec;
            }

            _timing.Clear();
            try
            {
                var timingJson = await _assetLoader.ReadTextAsync(AssetUri($"Assets/{folder}/clip_timing.json"));
                var tj = JObject.Parse(timingJson);
                foreach (var p in tj.Properties())
                    if (p.Value is JObject o && !p.Name.StartsWith("_", StringComparison.Ordinal))
                        _timing[p.Name] = ((int?)o["speakStartMs"] ?? 0,
                                           (int?)o["speakEndMs"] ?? 0,
                                           (int?)o["durationMs"] ?? 0);
            }
            catch { /* timing file optional */ }

            _expressive = _expressive.Where(ClipExistsAsync).Distinct().ToList();
            _clickEmotes.RemoveAll(c => !ClipExistsAsync(c));

            _knownClips.Clear();
            foreach (var (clip, _) in _idle) _knownClips.Add(clip);
            foreach (var c in _talkPool) _knownClips.Add(c);
            foreach (var kv in _stemPrefix) if (!string.IsNullOrEmpty(kv.Value)) _knownClips.Add(kv.Value);
            foreach (var v in _moodMap.Values) if (!string.IsNullOrEmpty(v)) _knownClips.Add(v);
            foreach (var c in _clickEmotes) _knownClips.Add(c);
            foreach (var c in _expressive) _knownClips.Add(c);

            _emoteHasLayout = false;
            if (j["layout"] is JObject ly)
            {
                _emoteScaleMul = (double?)ly["scale"] ?? 1.0;
                _emoteOffX = (int?)ly["offsetX"] ?? 0;
                _emoteOffY = (int?)ly["offsetY"] ?? 0;
                _emoteDetX = (int?)ly["detachedX"] ?? 0;
                _emoteDetY = (int?)ly["detachedY"] ?? 0;
                _emoteHasLayout = true;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to load emote map for {Folder}", folder);
            return false;
        }
    }

    private Uri AssetUri(string relativePath)
        => new($"avares://CCP.Avalonia/{relativePath}", UriKind.Absolute);

    private bool ClipExistsAsync(string clip)
        => _assetLoader.Exists(AssetUri($"Assets/{_folder}/{clip}.gif"));

    private static List<string> ParseClipList(JToken? token, List<string> fallback)
    {
        if (token == null) return fallback;
        if (token.Type == JTokenType.Array)
        {
            var l = token.Select(x => (string?)x).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
            return l.Count > 0 ? l : fallback;
        }
        var single = (string?)token;
        return string.IsNullOrEmpty(single) ? fallback : new List<string> { single! };
    }

    private void StartTalkSequence(List<string> talk, string? reaction, double durationSec)
    {
        if (talk.Count == 0) talk = new List<string> { "talkA" };
        StopTalkSequence();

        AudioLeadInMs = 0;
        int vms = (int)Math.Round(Math.Max(0, durationSec) * 1000);
        int startDelay = Math.Max(CurrentClipRemainingHold(), _talkStartDelayMs);

        if (vms - startDelay - _talkLeadOutMs < MinTalkWindowMs)
        {
            StartReactionOnly(reaction ?? PickWeightedIdle(), 0);
            return;
        }

        void Begin()
        {
            if (!_isActive) return;
            _talkSeqActive = true;
            _talkSchedule.Clear();

            long deadline = vms - _talkLeadOutMs - startDelay;
            var t0 = TalkTiming(talk[0]);
            long coveredEnd = t0.endMs;
            long lastStart = 0;

            for (int i = 1; i < talk.Count; i++)
            {
                var ti = TalkTiming(talk[i]);
                long at = Math.Max(lastStart + _minClipMs, coveredEnd - ti.startMs);
                if (at >= deadline) break;
                _talkSchedule.Enqueue((at, talk[i], false));
                lastStart = at;
                coveredEnd = at + ti.endMs;
            }

            if (coveredEnd + 250 < deadline)
            {
                var last = TalkTiming(talk[^1]);
                long at = Math.Max(lastStart + _minClipMs, coveredEnd - last.startMs);
                if (at < deadline) _talkSchedule.Enqueue((at, talk[^1], false));
            }

            _talkSchedule.Enqueue((deadline, reaction ?? PickWeightedIdle(), true));
            _talkSeqStartTick = Environment.TickCount64;
            CrossfadeTo(talk[0]);
            ArmTalkTimer();
        }

        DeferStart(Begin, startDelay);
    }

    private void StartReactionOnly(string clip, int leadInMs)
    {
        StopTalkSequence();
        int defer = CurrentClipRemainingHold();
        AudioLeadInMs = Math.Clamp(defer + leadInMs, 0, 3000);

        void Begin()
        {
            if (!_isActive) return;
            _queue.Clear();
            _talkSeqActive = false;
            CrossfadeTo(clip);
        }

        if (defer > 0) DeferStart(Begin, defer); else Begin();
    }

    private void StopTalkSequence()
    {
        _talkTimer.Stop();
        _startTimer.Stop();
        _pendingStart = null;
        _talkSeqActive = false;
        _talkSchedule.Clear();
    }

    private void DeferStart(Action begin, int ms)
    {
        _pendingStart = begin;
        _startTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
        _startTimer.Start();
    }

    private void OnStartTimerTick()
    {
        _startTimer.Stop();
        var a = _pendingStart; _pendingStart = null;
        if (_isActive) a?.Invoke();
    }

    private int CurrentClipRemainingHold()
    {
        if (_currentClip == null) return 0;
        long elapsed = Environment.TickCount64 - _clipStartTick;
        return (int)Math.Clamp(_minClipMs - elapsed, 0, _minClipMs);
    }

    private (int startMs, int endMs, int durMs) TalkTiming(string clip)
    {
        if (_timing.TryGetValue(clip, out var t))
        {
            int dur = t.durMs > 0 ? t.durMs : TalkFallbackLenMs;
            int start = Math.Clamp(t.startMs, 0, Math.Max(0, dur - 1));
            int end = t.endMs > start ? Math.Min(t.endMs, dur) : dur;
            return (start, end, dur);
        }
        return (TalkFallbackStartMs, TalkFallbackStartMs + TalkFallbackLenMs, TalkFallbackStartMs + TalkFallbackLenMs);
    }

    private int TalkLenMs(string clip)
    {
        var t = TalkTiming(clip);
        return t.endMs - t.startMs;
    }

    private List<string> PlanTalk(double durationSec)
    {
        var pool = _talkPool.Where(c => _knownClips.Contains(c)).ToList();
        if (pool.Count == 0) return new List<string> { "talkA" };
        int vms = (int)Math.Round(Math.Max(0, durationSec) * 1000);

        var snappy = pool.Where(c => TalkTiming(c).startMs <= TalkFastStartMs).ToList();
        string first = snappy.Count > 0
            ? snappy[_random.Next(snappy.Count)]
            : pool.OrderBy(c => TalkTiming(c).startMs).First();

        var picks = new List<string> { first };
        int covered = TalkLenMs(first);

        var rest = pool.Where(c => c != first).ToList();
        for (int i = rest.Count - 1; i > 0; i--)
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

    private string? ResolveReaction(string? emotionLineId, string? mood)
    {
        if (!string.IsNullOrEmpty(emotionLineId))
        {
            var stem = emotionLineId!.ToLowerInvariant();
            foreach (var kv in _stemPrefix)
                if (stem.StartsWith(kv.Key, StringComparison.Ordinal) && _knownClips.Contains(kv.Value))
                    return kv.Value;
        }
        if (!string.IsNullOrWhiteSpace(mood))
        {
            var token = mood!.Split(',')[0].Trim();
            if (_moodMap.TryGetValue(token, out var clip) && _knownClips.Contains(clip))
                return clip;
        }
        return null;
    }

    private string? PickExpressive()
    {
        var pool = _expressive.Where(c => _knownClips.Contains(c) && c != _currentClip).ToList();
        if (pool.Count == 0) pool = _expressive.Where(c => _knownClips.Contains(c)).ToList();
        return pool.Count == 0 ? null : pool[_random.Next(pool.Count)];
    }

    private string PickWeightedIdle()
    {
        if (_idle.Count == 0) return "idle";
        var pool = _idle.Where(x => x.clip != _currentClip).ToList();
        if (pool.Count == 0) pool = _idle;
        int total = pool.Sum(x => Math.Max(1, x.weight));
        int r = _random.Next(total);
        foreach (var (clip, weight) in pool)
        {
            r -= Math.Max(1, weight);
            if (r < 0) return clip;
        }
        return pool[0].clip;
    }

    private bool IsNonverbal(string? text, string? audioPath, double durationSec)
    {
        if (!string.IsNullOrWhiteSpace(text))
            return NonverbalRe.IsMatch(text.Trim());
        return !string.IsNullOrEmpty(audioPath) && durationSec > 0 && durationSec <= _nonverbalMaxSec;
    }

    private static readonly System.Text.RegularExpressions.Regex NonverbalRe =
        new(@"^(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+)(?:[\s,.~!\-]+(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+))*[\s,.~!\-]*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private void ArmTalkTimer()
    {
        if (_talkSchedule.Count == 0) return;
        long elapsed = Environment.TickCount64 - _talkSeqStartTick;
        long wait = Math.Max(1, _talkSchedule.Peek().atMs - elapsed);
        _talkTimer.Interval = TimeSpan.FromMilliseconds(wait);
        _talkTimer.Start();
    }

    private void OnTalkTimerTick()
    {
        _talkTimer.Stop();
        if (!_isActive || !_talkSeqActive || _talkSchedule.Count == 0)
        {
            _talkSeqActive = false;
            return;
        }
        var ev = _talkSchedule.Dequeue();
        if (ev.isReaction) _talkSeqActive = false;
        CrossfadeTo(ev.clip);
        if (_talkSeqActive) ArmTalkTimer();
    }

    private void OnWatchdogTick()
    {
        if (!_isActive || _talkSeqActive || _pendingClip != null) return;
        var activePlayer = GetPlayerFor(_activeImg);
        if (activePlayer?.IsComplete == true)
        {
            AdvanceQueue();
        }
    }

    private void AdvanceQueue()
    {
        if (!_isActive) return;
        if (_queue.Count > 0)
        {
            CrossfadeTo(_queue.Dequeue());
            return;
        }
        CrossfadeTo(PickWeightedIdle());
    }

    private void CrossfadeTo(string clip)
    {
        if (!_isActive || string.IsNullOrEmpty(clip) || clip == _currentClip) return;

        long elapsed = Environment.TickCount64 - _clipStartTick;
        if (_currentClip != null && elapsed < MinHoldMs)
        {
            _pendingClip = clip;
            _minHoldTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, MinHoldMs - elapsed));
            _minHoldTimer.Start();
            return;
        }

        DoCrossfade(clip);
    }

    private Task CrossfadeToAsync(string clip)
    {
        CrossfadeTo(clip);
        return Task.CompletedTask;
    }

    private void OnMinHoldTimerTick()
    {
        _minHoldTimer.Stop();
        if (!_isActive) return;
        var c = _pendingClip; _pendingClip = null;
        if (!string.IsNullOrEmpty(c) && c != _currentClip)
            CrossfadeTo(c);
    }

    private async void DoCrossfade(string clip)
    {
        if (!_isActive) return;
        try
        {
            var tempPath = await EnsureClipOnDiskAsync(clip);
            if (string.IsNullOrEmpty(tempPath))
            {
                _logger?.Warning("Emote clip missing: {Clip}", clip);
                AdvanceQueue();
                return;
            }

            var nextPlayer = AvaloniaAnimatedGif.TryCreate(tempPath, playOnce: true);
            if (nextPlayer == null)
            {
                _logger?.Warning("Emote clip decode failed: {Clip}", clip);
                AdvanceQueue();
                return;
            }

            nextPlayer.Completed += (_, _) =>
            {
                if (_isActive && !_talkSeqActive && _currentClip == clip)
                    AdvanceQueue();
            };

            var outgoing = _activeImg;
            var incoming = _inactiveImg;
            bool aIsActive = ReferenceEquals(outgoing, _layerA);

            incoming.Source = nextPlayer.Source;
            incoming.Opacity = 0;
            incoming.IsVisible = true;

            var oldPlayer = GetPlayerFor(incoming);
            SetPlayerFor(incoming, nextPlayer);

            _fadeOut?.Cancel();
            _fadeIn?.Cancel();
            _fadeOut = new OpacityFade(outgoing, outgoing.Opacity, 0, _fadeMs);
            _fadeIn = new OpacityFade(incoming, 0, 1, _fadeMs, () =>
            {
                _activeImg = incoming;
                _inactiveImg = outgoing;
                _currentClip = clip;
                _clipStartTick = Environment.TickCount64;
                ClipStarted?.Invoke(clip);
                oldPlayer?.Dispose();
            });
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Emote crossfade failed for {Clip}", clip);
        }
    }

    private async Task<string?> EnsureClipOnDiskAsync(string clip)
    {
        if (string.IsNullOrEmpty(_folder)) return null;
        var assetUri = AssetUri($"Assets/{_folder}/{clip}.gif");
        if (!_assetLoader.Exists(assetUri)) return null;

        var tempDir = Path.Combine(Path.GetTempPath(), "CCP.Avalonia", _folder);
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"{clip}.gif");
        if (File.Exists(tempPath)) return tempPath;

        try
        {
            await using var src = _assetLoader.Open(assetUri);
            await using var dst = File.Create(tempPath);
            await src.CopyToAsync(dst);
            return tempPath;
        }
        catch
        {
            return null;
        }
    }

    private AvaloniaAnimatedGif? GetPlayerFor(Image img) => ReferenceEquals(img, _layerA) ? _playerA : _playerB;

    private void SetPlayerFor(Image img, AvaloniaAnimatedGif? player)
    {
        if (ReferenceEquals(img, _layerA)) _playerA = player;
        else _playerB = player;
    }

    private static void DisposePlayer(ref AvaloniaAnimatedGif? player)
    {
        player?.Dispose();
        player = null;
    }

    private static double EstimateDurationSec(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 2.5;
        int words = text!.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Clamp(0.45 * words + 0.8, 2.0, 7.0);
    }

    private double EstimateAudioDurationSec(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            var info = new FileInfo(path);
            // Rough MP3 estimate: ~16 KB/s for 128 kbps. Acceptable for mouth timing.
            return Math.Clamp(info.Length / 16000.0, 1.0, 30.0);
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        Leave();
        _watchdog.Stop();
        _talkTimer.Stop();
        _startTimer.Stop();
        _minHoldTimer.Stop();
    }
}
