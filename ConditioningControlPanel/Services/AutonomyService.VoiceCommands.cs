using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// "Hey Bambi" voice COMMAND layer (v1) — the user-initiated mic (wake-word / push-to-talk)
    /// first listens against a small closed command grammar and, if it hears one, drives an app
    /// feature and confirms in-character. Anything it doesn't recognise falls through to the
    /// existing Spoken-Mantra flow, so saying nothing useful still gets you a mantra.
    ///
    /// Stays squarely in the offline engine's sweet spot ("say a known thing -> trigger that"):
    /// the grammar is constrained to the intent aliases, so <see cref="SpeechService"/> returns one
    /// of them (or [unk]); we then pick the best intent with the same fuzzy <see cref="SpeechService.Similarity"/>
    /// the mantra mechanic uses. Self-protecting: needs speech available + loud speech to fire, and
    /// the safety word routes to the same teardown as the panic key, so a false positive is the SAFE
    /// direction. v2 will broaden the vocabulary and voice the confirmations.
    /// </summary>
    public partial class AutonomyService
    {
        /// <summary>An intent = a set of spoken aliases -> one app action + per-mod confirmation.</summary>
        private sealed class VoiceCommandIntent
        {
            public string Name = "";
            public string[] Aliases = Array.Empty<string>();
            /// <summary>Run on the UI thread when matched. Null + <see cref="IsMantra"/> = fall back to a mantra.</summary>
            public Action? Execute;
            public bool IsMantra;
            /// <summary>mod-key ("bambi"/"sissy"/"circe") -> confirmation line (text-only in v1).</summary>
            public Dictionary<string, string> Confirm = new();
        }

        // Minimum fuzzy similarity (grammar-constrained, so a real hit scores ~1.0).
        private const double VoiceCommandMatchThreshold = 0.62;

        private static List<VoiceCommandIntent>? _voiceCommandIntents;

        /// <summary>The v1 command set. Built lazily; Execute closures call the static App services.</summary>
        private static List<VoiceCommandIntent> VoiceCommandIntents => _voiceCommandIntents ??= new()
        {
            // Safety word — routes to the exact panic-key teardown. A false positive just stops things.
            new VoiceCommandIntent
            {
                Name = "panic",
                Aliases = new[] { "red", "stop everything", "stop it all", "shut it all down", "safe word", "i'm done" },
                Execute = () => App.MainWindowRef?.TriggerPanicFromRemote(),
                Confirm = new()
                {
                    ["bambi"] = "okay okay! all stop~ you're safe, cutie",
                    ["sissy"] = "shh... everything's off. you're safe now, good girl.",
                    ["circe"] = "stopped. you're safe.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "bubbles_on",
                Aliases = new[] { "show me some bubbles", "show me bubbles", "more bubbles", "turn on the bubbles", "start the bubbles", "give me bubbles" },
                Execute = () => App.Bubbles?.Start(bypassLevelCheck: true),
                Confirm = new()
                {
                    ["bambi"] = "yay! bubbles~ pop pop pop!",
                    ["sissy"] = "mmm, bubbles for my good girl~",
                    ["circe"] = "bubbles. as you asked.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "bubbles_off",
                Aliases = new[] { "stop the bubbles", "no more bubbles", "stop bubbles", "turn off the bubbles" },
                Execute = () => App.Bubbles?.Stop(),
                Confirm = new()
                {
                    ["bambi"] = "aww, no more bubbles~ okayy!",
                    ["sissy"] = "all done, good girl.",
                    ["circe"] = "bubbles off.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "video_on",
                Aliases = new[] { "show me a video", "play a video", "play a hypnotube video", "play hypnotube", "give me a video" },
                Execute = () => App.Video?.TriggerVideo(),
                Confirm = new()
                {
                    ["bambi"] = "ooh a video! watch closely~",
                    ["sissy"] = "eyes on the screen for me~",
                    ["circe"] = "watch. don't look away.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "video_off",
                Aliases = new[] { "stop the video", "stop video", "no more videos", "turn off the video" },
                Execute = () => App.Video?.Stop(),
                Confirm = new()
                {
                    ["bambi"] = "video's gone~ hehe",
                    ["sissy"] = "that's enough watching, good girl.",
                    ["circe"] = "off it goes.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "flash_on",
                Aliases = new[] { "show me flashes", "start the flashes", "turn on flashes", "flash for me" },
                Execute = () => App.Flash?.Start(),
                Confirm = new()
                {
                    ["bambi"] = "flashy flashy~ don't blink!",
                    ["sissy"] = "let it wash over you~",
                    ["circe"] = "flashes on. sink.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "flash_off",
                Aliases = new[] { "stop the flashes", "stop flashing", "no more flashes", "turn off flashes" },
                Execute = () => App.Flash?.Stop(),
                Confirm = new()
                {
                    ["bambi"] = "okayy, no more flashes~",
                    ["sissy"] = "rest your eyes, good girl.",
                    ["circe"] = "flashes off.",
                },
            },
            // Explicit mantra request — defer to the existing Spoken-Mantra flow (no confirm needed).
            new VoiceCommandIntent
            {
                Name = "mantra",
                Aliases = new[] { "give me a mantra", "make me say it", "i want a mantra", "let me say something" },
                IsMantra = true,
            },
        };

        /// <summary>"bambi" / "sissy" / "circe" for the active mod (defaults to sissy's voice).</summary>
        private static string ModKey()
        {
            var id = App.Mods?.ActiveModId ?? "";
            if (id.Contains("bambi", StringComparison.OrdinalIgnoreCase)) return "bambi";
            if (id.Contains("sissy", StringComparison.OrdinalIgnoreCase)) return "sissy";
            if (id.Contains("locked", StringComparison.OrdinalIgnoreCase)) return "circe";
            return "sissy";
        }

        /// <summary>
        /// Listen once against the command grammar and, if a command is recognised, run it and confirm.
        /// Returns true when handled (caller should NOT then run a mantra); false to fall through to the
        /// mantra flow (no match, an explicit mantra request, silence, or speech unavailable).
        /// </summary>
        private async Task<bool> TryHandleVoiceCommandAsync()
        {
            try
            {
                if (App.Speech?.IsAvailable != true) return false;

                var intents = VoiceCommandIntents;
                var grammar = intents.SelectMany(i => i.Aliases).Distinct().ToList();
                if (grammar.Count == 0) return false;

                var res = await App.Speech.RecognizeOneOfAsync(
                    grammar, new RecognizeOptions { Timeout = TimeSpan.FromSeconds(6) }).ConfigureAwait(false);

                if (res.Unavailable || !res.LoudEnough || string.IsNullOrWhiteSpace(res.Transcript))
                    return false;

                var heard = SpeechService.Normalize(res.Transcript);
                VoiceCommandIntent? best = null;
                double bestScore = 0;
                foreach (var intent in intents)
                    foreach (var alias in intent.Aliases)
                    {
                        var s = SpeechService.Similarity(SpeechService.Normalize(alias), heard);
                        if (s > bestScore) { bestScore = s; best = intent; }
                    }

                if (best == null || bestScore < VoiceCommandMatchThreshold)
                {
                    App.Logger?.Information(
                        "AutonomyService: voice command no-match (heard '{Heard}', best {Score:0.00}) — falling back to mantra",
                        heard, bestScore);
                    return false;
                }

                App.Logger?.Information("AutonomyService: voice command '{Name}' (heard '{Heard}', score {Score:0.00})",
                    best.Name, heard, bestScore);

                // Explicit "give me a mantra" -> let the funnel run the Spoken-Mantra flow.
                if (best.IsMantra) return false;

                var modKey = ModKey();
                var confirm = best.Confirm.TryGetValue(modKey, out var c) && !string.IsNullOrWhiteSpace(c)
                    ? c
                    : best.Confirm.Values.FirstOrDefault() ?? "okay~";

                var intentToRun = best; // capture for the closure
                if (Application.Current?.Dispatcher != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try { intentToRun.Execute?.Invoke(); }
                        catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: voice command '{Name}' execute failed", intentToRun.Name); }
                        // Text-only confirmation in v1 (voiced in v2).
                        try { App.AvatarWindow?.GigglePriority(confirm, false, aiGenerated: false); } catch { }
                    });
                }
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AutonomyService: TryHandleVoiceCommandAsync failed");
                return false;
            }
        }
    }
}
