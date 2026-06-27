using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json.Linq;
using Vosk;

namespace ConditioningControlPanel.Services.Speech
{
    /// <summary>Outcome of a single "say this" listen window.</summary>
    public sealed class PhraseResult
    {
        /// <summary>True when the spoken phrase was close enough AND loud enough.</summary>
        public bool Matched { get; init; }
        /// <summary>What Vosk thinks the user said (best effort, may be empty).</summary>
        public string Transcript { get; init; } = "";
        /// <summary>Fuzzy similarity to the target phrase, 0..1.</summary>
        public double Score { get; init; }
        /// <summary>Vosk's own average per-word acoustic confidence, 0..1.</summary>
        public double Confidence { get; init; }
        /// <summary>Peak loudness during the window cleared the gate.</summary>
        public bool LoudEnough { get; init; }
        /// <summary>The listen window expired before a final utterance.</summary>
        public bool TimedOut { get; init; }
        /// <summary>Service unavailable (no model / no mic / not consented) — caller should skip, not fail.</summary>
        public bool Unavailable { get; init; }

        public static PhraseResult NotAvailable => new() { Unavailable = true };
    }

    /// <summary>Per-listen tuning. Null fields fall back to the service defaults / settings.</summary>
    public sealed class RecognizeOptions
    {
        /// <summary>Hard cap on the listen window.</summary>
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
        /// <summary>Minimum fuzzy similarity (0..1) to count as a match. Null => settings/default.</summary>
        public double? MatchThreshold { get; init; }
        /// <summary>Minimum peak RMS loudness (0..1) to count as "said out loud". Null => settings/default.</summary>
        public double? LoudnessThreshold { get; init; }
    }

    /// <summary>
    /// Offline speech recognition for the Takeover "repeat after me" mechanic.
    ///
    /// Design contract (see memory takeover-rework-design):
    ///  - OFFLINE ONLY. Engine = Vosk (closed-grammar STT) + NAudio capture. No network, ever.
    ///  - "repeat after me" is VERIFICATION, not dictation: we already know the target phrase, so we
    ///    constrain Vosk's grammar to it and fuzzy-score the result. Misses become content, not bugs.
    ///  - Single owner: at most one capture session at a time (re-entrancy guarded).
    ///  - <see cref="IsListening"/> is runtime-only and NEVER persisted.
    ///  - Graceful no-op: no model on disk / no mic / consent not given => <see cref="IsAvailable"/> false
    ///    and every recognize call returns <see cref="PhraseResult.NotAvailable"/>.
    ///  - Privacy: audio buffers stay in memory, are never written to disk or transmitted.
    /// </summary>
    public sealed class SpeechService : IDisposable
    {
        private const int SampleRate = 16000; // Vosk small models are 16 kHz mono.

        private readonly object _gate = new();
        private Model? _model;
        private bool _modelLoadAttempted;
        private bool _disposed;

        // Only one capture session may run at a time.
        private int _sessionActive; // 0/1 via Interlocked.

        // The current session's cancellation, exposed so a UI "stop the mic" affordance (the
        // privacy pill) can cut an in-flight capture. Linked to the caller's token; null when idle.
        private CancellationTokenSource? _activeCts;

        /// <summary>Streaming partial hypotheses while a window is open ("I'm hearing ___").</summary>
        public event EventHandler<string>? PartialTranscript;
        /// <summary>Live input loudness, 0..1 RMS, for a level meter.</summary>
        public event EventHandler<double>? LevelChanged;
        /// <summary>Raised when IsListening flips, for UI state.</summary>
        public event EventHandler<bool>? ListeningChanged;

        private bool _isListening;
        /// <summary>True only while a capture window is open. Runtime-only; never persisted.</summary>
        public bool IsListening
        {
            get => _isListening;
            private set
            {
                if (_isListening == value) return;
                _isListening = value;
                try { ListeningChanged?.Invoke(this, value); } catch { /* UI handler hygiene */ }
            }
        }

        /// <summary>
        /// True when speech recognition can actually run: a Vosk model is present on disk and
        /// at least one capture device exists. Does NOT check consent — callers gate on that.
        /// Lazily loads the model on first query and caches the result.
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                if (_disposed) return false;
                if (!HasCaptureDevice) return false;
                EnsureModel();
                return _model != null;
            }
        }

        /// <summary>
        /// Cancel any in-flight capture session immediately (UI "stop the mic" / privacy pill).
        /// Closes the open mic so <see cref="IsListening"/> flips false. No-op when idle. Continuous
        /// callers (the wake loop, lock-card voice solve) must also be told to stop arming, or they
        /// simply reopen the mic on the next iteration.
        /// </summary>
        public void StopListening()
        {
            try { _activeCts?.Cancel(); } catch { }
        }

        /// <summary>Whether the OS reports at least one audio capture device.</summary>
        public static bool HasCaptureDevice
        {
            get
            {
                try { return WaveInEvent.DeviceCount > 0; }
                catch { return false; }
            }
        }

        /// <summary>Directory we expect the Vosk model to live in (drop the unpacked model here).</summary>
        public static string ModelRoot =>
            Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "vosk");

        public SpeechService()
        {
            // Silence Vosk's native chatter on stdout; never throw from the ctor.
            try { Vosk.Vosk.SetLogLevel(-1); } catch { /* native lib may be absent in odd builds */ }
        }

        /// <summary>Resolve and load the model directory once. Safe to call repeatedly.</summary>
        private void EnsureModel()
        {
            if (_model != null || _modelLoadAttempted) return;
            lock (_gate)
            {
                if (_model != null || _modelLoadAttempted) return;
                _modelLoadAttempted = true;
                try
                {
                    var dir = ResolveModelDir();
                    if (dir == null)
                    {
                        App.Logger?.Information("SpeechService: no Vosk model found under {Root} — speech disabled", ModelRoot);
                        return;
                    }
                    _model = new Model(dir);
                    App.Logger?.Information("SpeechService: Vosk model loaded from {Dir}", dir);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "SpeechService: failed to load Vosk model — speech disabled");
                    _model = null;
                }
            }
        }

        /// <summary>
        /// A valid Vosk model dir contains both an "am" and a "conf" folder. Accept either the
        /// model root itself or a single nested folder (the way the official zips unpack).
        /// </summary>
        private static string? ResolveModelDir()
        {
            if (!Directory.Exists(ModelRoot)) return null;
            if (LooksLikeModel(ModelRoot)) return ModelRoot;
            foreach (var sub in Directory.EnumerateDirectories(ModelRoot))
                if (LooksLikeModel(sub)) return sub;
            return null;
        }

        private static bool LooksLikeModel(string dir) =>
            Directory.Exists(Path.Combine(dir, "am")) && Directory.Exists(Path.Combine(dir, "conf"));

        /// <summary>
        /// Open the mic, ask the user to say <paramref name="target"/>, and score what comes back.
        /// Returns when an utterance finalizes, the user clears the bar, or the window times out.
        /// Never throws — failures degrade to <see cref="PhraseResult.NotAvailable"/> or a timeout.
        /// </summary>
        public Task<PhraseResult> RecognizePhraseAsync(string target, RecognizeOptions? options = null, CancellationToken ct = default)
            => RunGrammarSessionAsync(target, new[] { target }, options, isWakeWord: false, ct);

        /// <summary>
        /// Listen once and return the best-effort transcript constrained to <paramref name="phrases"/>
        /// (a closed command set, e.g. the "Hey Bambi" voice-command grammar). Unlike
        /// <see cref="RecognizePhraseAsync"/> this does not judge against one target — the caller
        /// inspects <see cref="PhraseResult.Transcript"/> (and <see cref="PhraseResult.LoudEnough"/>)
        /// and routes it to an intent itself (e.g. via <see cref="Similarity"/>). Never throws.
        /// </summary>
        public Task<PhraseResult> RecognizeOneOfAsync(IReadOnlyList<string> phrases, RecognizeOptions? options = null, CancellationToken ct = default)
            => RunGrammarSessionAsync(phrases.Count > 0 ? phrases[0] : "", phrases, options, isWakeWord: false, ct);

        /// <summary>
        /// Listen until one of <paramref name="wakeWords"/> is heard or the token cancels.
        /// Intended for the opt-in always-on "Hey Bambi" path. Returns the matched phrase, or null.
        /// </summary>
        public async Task<string?> WaitForWakeWordAsync(IEnumerable<string> wakeWords, CancellationToken ct = default)
        {
            var words = wakeWords?.Where(w => !string.IsNullOrWhiteSpace(w)).Select(Normalize).Distinct().ToList()
                        ?? new List<string>();
            if (words.Count == 0) return null;
            var res = await RunGrammarSessionAsync(words[0], words, new RecognizeOptions { Timeout = Timeout.InfiniteTimeSpan, MatchThreshold = 0.8 }, isWakeWord: true, ct)
                .ConfigureAwait(false);
            return res.Matched ? (string.IsNullOrEmpty(res.Transcript) ? words[0] : res.Transcript) : null;
        }

        private async Task<PhraseResult> RunGrammarSessionAsync(
            string target, IReadOnlyList<string> grammarPhrases, RecognizeOptions? options, bool isWakeWord, CancellationToken ct)
        {
            options ??= new RecognizeOptions();
            if (!IsAvailable) return PhraseResult.NotAvailable;

            // Re-entrancy guard: refuse a second concurrent session rather than corrupt the recognizer.
            if (Interlocked.CompareExchange(ref _sessionActive, 1, 0) != 0)
            {
                App.Logger?.Warning("SpeechService: recognize requested while a session is already active — skipping");
                return PhraseResult.NotAvailable;
            }

            var matchThreshold = options.MatchThreshold ?? SettingDouble("SpeechMatchThreshold", 0.62);
            var loudnessThreshold = options.LoudnessThreshold ?? SettingDouble("SpeechLoudnessThreshold", 0.04);
            var normalizedTarget = Normalize(target);

            var tcs = new TaskCompletionSource<PhraseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            WaveInEvent? mic = null;
            VoskRecognizer? rec = null;
            double peakRms = 0;
            var done = 0;

            void Finish(PhraseResult r)
            {
                if (Interlocked.Exchange(ref done, 1) != 0) return;
                tcs.TrySetResult(r);
            }

            PhraseResult Evaluate(string transcript, double confidence, bool timedOut)
            {
                var heard = Normalize(transcript);
                var score = Similarity(normalizedTarget, heard);
                var loud = peakRms >= loudnessThreshold;
                return new PhraseResult
                {
                    Transcript = transcript?.Trim() ?? "",
                    Score = score,
                    Confidence = confidence,
                    LoudEnough = loud,
                    Matched = !timedOut && score >= matchThreshold && loud,
                    TimedOut = timedOut
                };
            }

            try
            {
                rec = BuildRecognizer(grammarPhrases);
                rec.SetWords(true);

                mic = new WaveInEvent
                {
                    DeviceNumber = ResolveDeviceNumber(),
                    WaveFormat = new WaveFormat(SampleRate, 16, 1),
                    BufferMilliseconds = 50
                };

                mic.DataAvailable += (_, e) =>
                {
                    if (Volatile.Read(ref done) != 0) return;
                    try
                    {
                        peakRms = Math.Max(peakRms, Rms(e.Buffer, e.BytesRecorded));
                        RaiseLevel(Rms(e.Buffer, e.BytesRecorded));

                        if (rec!.AcceptWaveform(e.Buffer, e.BytesRecorded))
                        {
                            var (text, conf) = ParseResult(rec.Result());
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                var result = Evaluate(text, conf, timedOut: false);
                                // For a discrete phrase we accept the first finalized utterance; for
                                // wake-word spotting we keep listening until we actually match.
                                if (!isWakeWord || result.Matched)
                                    Finish(result);
                            }
                        }
                        else
                        {
                            var partial = ParsePartial(rec.PartialResult());
                            if (!string.IsNullOrWhiteSpace(partial)) RaisePartial(partial);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "SpeechService: capture callback failed");
                        Finish(Evaluate("", 0, timedOut: true));
                    }
                };
                mic.RecordingStopped += (_, _) => { /* surfaced via Finish paths */ };

                // Link the caller's token with a session-scoped source so the UI can stop us
                // mid-capture (StopListening) no matter what token the caller passed.
                _activeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var ctReg = _activeCts.Token.Register(() => Finish(Evaluate("", 0, timedOut: true)));

                CancellationTokenSource? timeoutCts = null;
                if (options.Timeout != Timeout.InfiniteTimeSpan)
                {
                    timeoutCts = new CancellationTokenSource(options.Timeout);
                    timeoutCts.Token.Register(() =>
                    {
                        // On timeout, flush whatever Vosk has buffered as a last chance to match.
                        try
                        {
                            var (text, conf) = ParseResult(rec!.FinalResult());
                            Finish(Evaluate(text, conf, timedOut: string.IsNullOrWhiteSpace(text)));
                        }
                        catch { Finish(Evaluate("", 0, timedOut: true)); }
                    });
                }

                IsListening = true;
                mic.StartRecording();

                var final = await tcs.Task.ConfigureAwait(false);
                timeoutCts?.Dispose();
                return final;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "SpeechService: session setup failed");
                return PhraseResult.NotAvailable;
            }
            finally
            {
                IsListening = false;
                try { mic?.StopRecording(); } catch { }
                try { mic?.Dispose(); } catch { }
                try { rec?.Dispose(); } catch { }
                try { _activeCts?.Dispose(); } catch { }
                _activeCts = null;
                RaiseLevel(0);
                Interlocked.Exchange(ref _sessionActive, 0);
            }
        }

        /// <summary>
        /// Build a grammar-constrained recognizer where possible. Out-of-vocabulary words make the
        /// grammar ctor throw, so fall back to a free recognizer and lean on fuzzy scoring.
        /// </summary>
        private VoskRecognizer BuildRecognizer(IReadOnlyList<string> phrases)
        {
            var cleaned = phrases.Select(Normalize).Where(p => p.Length > 0).Distinct().ToList();
            if (cleaned.Count > 0)
            {
                try
                {
                    var grammar = new JArray();
                    foreach (var p in cleaned) grammar.Add(p);
                    grammar.Add("[unk]"); // lets Vosk emit "unknown" instead of forcing a wrong match
                    return new VoskRecognizer(_model, SampleRate, grammar.ToString(Newtonsoft.Json.Formatting.None));
                }
                catch (Exception ex)
                {
                    App.Logger?.Information(ex, "SpeechService: grammar recognizer failed, using free recognizer");
                }
            }
            return new VoskRecognizer(_model, SampleRate);
        }

        private int ResolveDeviceNumber()
        {
            var idx = SettingInt("SpeechInputDeviceIndex", -1);
            try
            {
                if (idx >= 0 && idx < WaveInEvent.DeviceCount) return idx;
            }
            catch { }
            return 0; // WaveIn device 0 == Windows default capture device.
        }

        private void RaisePartial(string text)
        {
            try { PartialTranscript?.Invoke(this, text); } catch { }
        }

        private void RaiseLevel(double level)
        {
            try { LevelChanged?.Invoke(this, Math.Clamp(level, 0, 1)); } catch { }
        }

        // --- result parsing -------------------------------------------------

        private static (string text, double conf) ParseResult(string json)
        {
            try
            {
                var o = JObject.Parse(json);
                var text = (string?)o["text"] ?? "";
                double conf = 0;
                if (o["result"] is JArray words && words.Count > 0)
                {
                    double sum = 0; int n = 0;
                    foreach (var w in words)
                    {
                        var c = (double?)w["conf"];
                        if (c.HasValue) { sum += c.Value; n++; }
                    }
                    if (n > 0) conf = sum / n;
                }
                return (text, conf);
            }
            catch { return ("", 0); }
        }

        private static string ParsePartial(string json)
        {
            try { return (string?)JObject.Parse(json)["partial"] ?? ""; }
            catch { return ""; }
        }

        // --- scoring --------------------------------------------------------

        /// <summary>Lowercase, strip punctuation, collapse whitespace — so scoring compares words, not noise.</summary>
        public static string Normalize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '\'') sb.Append(' ');
            }
            return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Word-level normalized similarity (0..1): 1 minus word edit distance over the longer phrase.
        /// Robust to a dropped/extra word, which is exactly how "repeat after me" misses look.
        /// </summary>
        public static double Similarity(string target, string heard)
        {
            if (target.Length == 0) return heard.Length == 0 ? 1 : 0;
            if (heard.Length == 0) return 0;
            var a = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var b = heard.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return b.Length == 0 ? 1 : 0;
            int dist = WordEditDistance(a, b);
            double wordSim = 1.0 - (double)dist / Math.Max(a.Length, b.Length);
            return Math.Clamp(wordSim, 0, 1);
        }

        private static int WordEditDistance(string[] a, string[] b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                }
                (prev, cur) = (cur, prev);
            }
            return prev[b.Length];
        }

        private static double Rms(byte[] buffer, int bytes)
        {
            if (bytes < 2) return 0;
            long samples = bytes / 2;
            double sumSq = 0;
            for (int i = 0; i + 1 < bytes; i += 2)
            {
                short s = (short)(buffer[i] | (buffer[i + 1] << 8));
                double v = s / 32768.0;
                sumSq += v * v;
            }
            return Math.Sqrt(sumSq / samples);
        }

        // --- settings access (kept defensive; settings may add these keys later) ---

        private static double SettingDouble(string name, double fallback)
        {
            try
            {
                var p = App.Settings?.Current?.GetType().GetProperty(name);
                if (p?.GetValue(App.Settings!.Current) is double d && d > 0) return d;
            }
            catch { }
            return fallback;
        }

        private static int SettingInt(string name, int fallback)
        {
            try
            {
                var p = App.Settings?.Current?.GetType().GetProperty(name);
                if (p?.GetValue(App.Settings!.Current) is int i) return i;
            }
            catch { }
            return fallback;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _model?.Dispose(); } catch { }
            _model = null;
        }
    }
}
