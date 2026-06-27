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
    /// "Hey Bambi" voice COMMAND layer (v2) — the user-initiated mic (wake-word / push-to-talk)
    /// first listens against a closed command grammar and, if it hears one, drives an app feature
    /// and confirms in-character. Anything it doesn't recognise falls through to the existing
    /// Spoken-Mantra flow, so saying nothing useful still gets you a mantra.
    ///
    /// Stays squarely in the offline engine's sweet spot ("say a known thing -> trigger that"):
    /// the grammar is constrained to the intent aliases, so <see cref="SpeechService"/> returns one
    /// of them (or [unk]); we then pick the best intent with the same fuzzy <see cref="SpeechService.Similarity"/>
    /// the mantra mechanic uses. Self-protecting: needs speech available + loud speech to fire, and
    /// the safety word routes to the same teardown as the panic key, so a false positive is the SAFE
    /// direction.
    ///
    /// v2 adds: a much wider verb set (start/stop most features, one-shots, session + volume control),
    /// command chaining (a short follow-up window so you can stack commands without re-waking), a
    /// polite re-listen on a near-miss, "again"/"more" to repeat the last command, "what can I say"
    /// help, and terse acks for utility verbs.
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
            /// <summary>"again"/"one more"/"more"/"harder" — re-run the last actionable command instead of a fixed action.</summary>
            public bool IsReplay;
            /// <summary>Don't open a follow-up (chaining) window after this one runs — e.g. panic, stop-listening.</summary>
            public bool NoChain;
            /// <summary>Eligible to be the target of a later "again"/"more". False for help, replay, panic, stop-listening.</summary>
            public bool Repeatable = true;
            /// <summary>Short, text-only confirmation (skips the voiced manifest lookup) — used for utility verbs
            /// like pause / mute / volume so they don't get a full giggled bark every time.</summary>
            public bool TerseAck;
            /// <summary>mod-key ("bambi"/"sissy"/"circe") -> confirmation line. Fallback text only —
            /// the live, voiced line is pulled from the bark manifest by <see cref="VoiceRuleId"/>.</summary>
            public Dictionary<string, string> Confirm = new();
            /// <summary>
            /// Bark-manifest rule id whose variant pool holds this command's voiced confirmations
            /// (text + per-mod audio). Picked via <see cref="BarkService.PickVoiceLine"/> so the
            /// spoken clip always matches the bubble. Null = use <see cref="Confirm"/>, text-only.
            /// </summary>
            public string? VoiceRuleId;
        }

        // Minimum fuzzy similarity (grammar-constrained, so a real hit scores ~1.0). Kept at 0.62:
        // a higher gate mostly costs legitimate recognitions (a 3-word alias missing one word scores
        // 0.667), and it does NOT prevent cross-talk — near-neighbours ("stop the video" vs "stop the
        // spiral") collide at Vosk's acoustic layer at ~1.0, below this check. Distinct phrasing, not a
        // tighter threshold, is what keeps commands apart; this gate only rejects [unk]/garbage.
        private const double VoiceCommandMatchThreshold = 0.62;

        // After a successful command we keep listening for a few quick follow-ups (no wake word needed)
        // so you can stack "bubbles ... flashes ... deeper" in one breath. Capped so it always winds down.
        private const int MaxChainedCommands = 3;

        // The last actionable command run this session — the target of "again" / "one more" / "more".
        private static VoiceCommandIntent? _lastVoiceIntent;

        private static List<VoiceCommandIntent>? _voiceCommandIntents;

        /// <summary>The command set. Built lazily; Execute closures call the static App services.</summary>
        private static List<VoiceCommandIntent> VoiceCommandIntents => _voiceCommandIntents ??= new()
        {
            // ── Safety ────────────────────────────────────────────────────────────
            // Routes to the exact panic-key teardown. A false positive just stops things.
            new VoiceCommandIntent
            {
                Name = "panic",
                Aliases = new[] { "red", "stop everything", "stop it all", "shut it all down", "safe word", "i'm done" },
                Execute = () => App.MainWindowRef?.TriggerPanicFromRemote(),
                VoiceRuleId = "voicecmd_panic",
                NoChain = true,
                Repeatable = false,
                Confirm = new()
                {
                    ["bambi"] = "okay okay! all stop~ you're safe, cutie",
                    ["sissy"] = "shh... everything's off. you're safe now, good girl.",
                    ["circe"] = "stopped. you're safe.",
                },
            },

            // ── Bubbles ───────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "bubbles_on",
                Aliases = new[] { "show me some bubbles", "show me bubbles", "more bubbles", "turn on the bubbles", "start the bubbles", "give me bubbles" },
                Execute = () => App.Bubbles?.Start(bypassLevelCheck: true),
                VoiceRuleId = "voicecmd_bubbles_on",
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
                VoiceRuleId = "voicecmd_bubbles_off",
                Confirm = new()
                {
                    ["bambi"] = "aww, no more bubbles~ okayy!",
                    ["sissy"] = "all done, good girl.",
                    ["circe"] = "bubbles off.",
                },
            },

            // ── Video ─────────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "video_on",
                Aliases = new[] { "show me a video", "play a video", "play a hypnotube video", "play hypnotube", "give me a video" },
                Execute = () => App.Video?.TriggerVideo(),
                VoiceRuleId = "voicecmd_video_on",
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
                VoiceRuleId = "voicecmd_video_off",
                Confirm = new()
                {
                    ["bambi"] = "video's gone~ hehe",
                    ["sissy"] = "that's enough watching, good girl.",
                    ["circe"] = "off it goes.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "video_pause",
                Aliases = new[] { "pause the video", "pause video", "pause this video" },
                Execute = () => App.Video?.PausePrimary(),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "video on hold~ hehe",
                    ["sissy"] = "paused for you, good girl~",
                    ["circe"] = "video paused.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "video_resume",
                Aliases = new[] { "resume the video", "play the video", "unpause the video", "continue the video" },
                Execute = () => App.Video?.PlayPrimary(),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "playing again~ watch!",
                    ["sissy"] = "eyes back on it, good girl~",
                    ["circe"] = "video resumed.",
                },
            },

            // ── Flashes ───────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "flash_on",
                Aliases = new[] { "show me flashes", "start the flashes", "turn on flashes", "flash for me" },
                Execute = () => App.Flash?.Start(),
                VoiceRuleId = "voicecmd_flash_on",
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
                VoiceRuleId = "voicecmd_flash_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, no more flashes~",
                    ["sissy"] = "rest your eyes, good girl.",
                    ["circe"] = "flashes off.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "flash_once",
                Aliases = new[] { "flash me", "one flash", "give me a flash", "flash once" },
                Execute = () => App.Flash?.TriggerFlash(),
                VoiceRuleId = "voicecmd_flash_once",
                Confirm = new()
                {
                    ["bambi"] = "blink~! hehe",
                    ["sissy"] = "there~ did you catch it, good girl?",
                    ["circe"] = "flash.",
                },
            },

            // ── Subliminals ─────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "subliminals_on",
                Aliases = new[] { "show me subliminals", "turn on subliminals", "start the subliminals", "subliminals on" },
                Execute = () => App.Subliminal?.Start(),
                VoiceRuleId = "voicecmd_subliminals_on",
                Confirm = new()
                {
                    ["bambi"] = "sneaky words~ they go riiight in!",
                    ["sissy"] = "let the words sink in, good girl~",
                    ["circe"] = "subliminals on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "subliminals_off",
                Aliases = new[] { "stop the subliminals", "turn off subliminals", "no more subliminals", "subliminals off" },
                Execute = () => App.Subliminal?.Stop(),
                VoiceRuleId = "voicecmd_subliminals_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, words off~",
                    ["sissy"] = "that's enough for now, good girl.",
                    ["circe"] = "subliminals off.",
                },
            },

            // ── Bouncing text ───────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "bouncing_on",
                Aliases = new[] { "turn on the bouncing text", "start the bouncing text", "show me bouncing text", "bouncing text on" },
                Execute = () => App.BouncingText?.Start(),
                VoiceRuleId = "voicecmd_bouncing_on",
                Confirm = new()
                {
                    ["bambi"] = "boing boing words~ wheee!",
                    ["sissy"] = "watch them dance for you~",
                    ["circe"] = "bouncing text on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "bouncing_off",
                Aliases = new[] { "turn off the bouncing text", "stop the bouncing text", "no more bouncing text", "bouncing text off" },
                Execute = () => App.BouncingText?.Stop(),
                VoiceRuleId = "voicecmd_bouncing_off",
                Confirm = new()
                {
                    ["bambi"] = "aww okay, no more boingy words~",
                    ["sissy"] = "all settled, good girl.",
                    ["circe"] = "bouncing text off.",
                },
            },

            // ── Spiral overlay ──────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "spiral_on",
                Aliases = new[] { "show me the spiral", "turn on the spiral", "start the spiral", "spiral on" },
                Execute = () => App.Overlay?.ShowOverlaySustained("spiral", 0.5),
                VoiceRuleId = "voicecmd_spiral_on",
                Confirm = new()
                {
                    ["bambi"] = "spirally~ look how pretty it spins!",
                    ["sissy"] = "follow the spiral down, good girl~",
                    ["circe"] = "spiral on. look into it.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "spiral_off",
                Aliases = new[] { "hide the spiral", "turn off the spiral", "stop the spiral", "spiral off" },
                Execute = () => App.Overlay?.HideOverlaySustained("spiral"),
                VoiceRuleId = "voicecmd_spiral_off",
                Confirm = new()
                {
                    ["bambi"] = "spiral's gone~ poof!",
                    ["sissy"] = "eyes back to me now, good girl.",
                    ["circe"] = "spiral off.",
                },
            },

            // ── Pink filter ─────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "pink_on",
                Aliases = new[] { "turn on the pink filter", "show me the pink filter", "pink filter on", "make it pink" },
                Execute = () => App.Overlay?.ShowOverlaySustained("pink", 0.4),
                VoiceRuleId = "voicecmd_pink_on",
                Confirm = new()
                {
                    ["bambi"] = "everything's pink now~ so cute!",
                    ["sissy"] = "bathe in the pink, good girl~",
                    ["circe"] = "pink filter on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "pink_off",
                Aliases = new[] { "turn off the pink filter", "hide the pink filter", "pink filter off" },
                Execute = () => App.Overlay?.HideOverlaySustained("pink"),
                VoiceRuleId = "voicecmd_pink_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, un-pink~",
                    ["sissy"] = "back to normal, good girl.",
                    ["circe"] = "pink filter off.",
                },
            },

            // ── Mind wipe ───────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "mindwipe_on",
                Aliases = new[] { "turn on mind wipe", "start the mind wipe", "mind wipe on" },
                Execute = () => App.MindWipe?.Start(30.0, (App.Settings?.Current?.MindWipeVolume ?? 50) / 100.0),
                VoiceRuleId = "voicecmd_mindwipe_on",
                Confirm = new()
                {
                    ["bambi"] = "ooh, wipey wipey~ bye bye thoughts!",
                    ["sissy"] = "let me empty that pretty head, good girl~",
                    ["circe"] = "mind wipe on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "mindwipe_off",
                Aliases = new[] { "turn off mind wipe", "stop the mind wipe", "mind wipe off" },
                Execute = () => App.MindWipe?.Stop(),
                VoiceRuleId = "voicecmd_mindwipe_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, thoughts can come back~",
                    ["sissy"] = "all done, good girl.",
                    ["circe"] = "mind wipe off.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "wipe_once",
                Aliases = new[] { "wipe my mind", "wipe me", "empty my head", "blank my mind" },
                Execute = () => App.MindWipe?.TriggerOnce(),
                VoiceRuleId = "voicecmd_wipe_once",
                Confirm = new()
                {
                    ["bambi"] = "poof~! all gone, hehe",
                    ["sissy"] = "empty and pretty, good girl~",
                    ["circe"] = "blank.",
                },
            },

            // ── Lock cards ──────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "lockcard_on",
                Aliases = new[] { "turn on lock cards", "start lock card mode", "lock card mode on", "enable lock cards" },
                Execute = () => App.LockCard?.Start(),
                VoiceRuleId = "voicecmd_lockcard_on",
                Confirm = new()
                {
                    ["bambi"] = "lock cards on~ get ready to say it!",
                    ["sissy"] = "time to prove yourself, good girl~",
                    ["circe"] = "lock cards armed.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "lockcard_off",
                Aliases = new[] { "turn off lock cards", "stop lock card mode", "lock card mode off", "disable lock cards" },
                Execute = () => App.LockCard?.Stop(),
                VoiceRuleId = "voicecmd_lockcard_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, no more lock cards~",
                    ["sissy"] = "you're released, good girl.",
                    ["circe"] = "lock cards off.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "lock_once",
                Aliases = new[] { "lock me", "lock card now", "give me a lock card", "show me a lock card" },
                Execute = () => App.LockCard?.ShowLockCard(),
                VoiceRuleId = "voicecmd_lock_once",
                Confirm = new()
                {
                    ["bambi"] = "say it for me~ go go go!",
                    ["sissy"] = "prove it to me, good girl~",
                    ["circe"] = "say it. now.",
                },
            },

            // ── Pop quiz ────────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "popquiz_on",
                Aliases = new[] { "turn on pop quizzes", "start pop quiz mode", "pop quiz mode on", "enable pop quizzes" },
                Execute = () => App.PopQuiz?.Start(),
                VoiceRuleId = "voicecmd_popquiz_on",
                Confirm = new()
                {
                    ["bambi"] = "quiz time soon~ study up!",
                    ["sissy"] = "i'll be testing you, good girl~",
                    ["circe"] = "pop quizzes on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "popquiz_off",
                Aliases = new[] { "turn off pop quizzes", "stop pop quiz mode", "pop quiz mode off", "disable pop quizzes" },
                Execute = () => App.PopQuiz?.Stop(),
                VoiceRuleId = "voicecmd_popquiz_off",
                Confirm = new()
                {
                    ["bambi"] = "no more quizzes~ phew!",
                    ["sissy"] = "no more tests for now, good girl.",
                    ["circe"] = "pop quizzes off.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "quiz_once",
                Aliases = new[] { "quiz me", "quiz me now", "give me a quiz", "test me" },
                Execute = () => App.PopQuiz?.ShowPopQuiz(),
                VoiceRuleId = "voicecmd_quiz_once",
                Confirm = new()
                {
                    ["bambi"] = "pop quiz~! hehe ready?",
                    ["sissy"] = "let's see what you remember, good girl~",
                    ["circe"] = "answer this.",
                },
            },

            // ── Keyword triggers ────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "keyword_on",
                Aliases = new[] { "turn on keyword triggers", "start keyword triggers", "keyword triggers on", "enable keyword triggers" },
                Execute = () => App.KeywordTriggers?.Start(),
                VoiceRuleId = "voicecmd_keyword_on",
                Confirm = new()
                {
                    ["bambi"] = "trigger words armed~ ooh!",
                    ["sissy"] = "your words have power now, good girl~",
                    ["circe"] = "keyword triggers on.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "keyword_off",
                Aliases = new[] { "turn off keyword triggers", "stop keyword triggers", "keyword triggers off", "disable keyword triggers" },
                Execute = () => App.KeywordTriggers?.Stop(),
                VoiceRuleId = "voicecmd_keyword_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, words are safe again~",
                    ["sissy"] = "triggers disarmed, good girl.",
                    ["circe"] = "keyword triggers off.",
                },
            },

            // ── One-shot toys ───────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "count_once",
                Aliases = new[] { "count for me", "count the bubbles", "give me a counting game", "make me count" },
                Execute = () => App.BubbleCount?.TriggerGame(),
                VoiceRuleId = "voicecmd_count_once",
                Confirm = new()
                {
                    ["bambi"] = "counting time~ one, two, ooh!",
                    ["sissy"] = "count them all for me, good girl~",
                    ["circe"] = "count them.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "freeze_once",
                Aliases = new[] { "freeze", "freeze me", "freeze for me", "bambi freeze" },
                Execute = () => App.Subliminal?.TriggerBambiFreeze(),
                VoiceRuleId = "voicecmd_freeze_once",
                Confirm = new()
                {
                    ["bambi"] = "freeze~! good girl, don't move!",
                    ["sissy"] = "still now, good girl. freeze~",
                    ["circe"] = "freeze.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "shake_once",
                Aliases = new[] { "shake the screen", "shake it", "shake me", "earthquake" },
                Execute = () => App.ScreenShake?.Shake(60, 1200),
                VoiceRuleId = "voicecmd_shake_once",
                Confirm = new()
                {
                    ["bambi"] = "wheee~ shakey shakey!",
                    ["sissy"] = "feel that, good girl~",
                    ["circe"] = "shaking.",
                },
            },

            // ── Deeper ──────────────────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "deeper",
                Aliases = new[] { "deeper", "go deeper", "take me deeper", "sink deeper", "drop deeper" },
                Execute = () => App.BrainDrain?.Start(bypassLevelCheck: true),
                VoiceRuleId = "voicecmd_deeper",
                Confirm = new()
                {
                    ["bambi"] = "deeper~ down down down, hehe",
                    ["sissy"] = "that's it... deeper for me, good girl~",
                    ["circe"] = "deeper. sink.",
                },
            },

            // ── Takeover (Autonomy) ─────────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "takeover_on",
                Aliases = new[] { "take over", "take control", "you're in charge", "take over for me" },
                Execute = () => App.Autonomy?.Start(),
                VoiceRuleId = "voicecmd_takeover_on",
                Confirm = new()
                {
                    ["bambi"] = "ooh, my turn~ i've got you now, hehe!",
                    ["sissy"] = "good girl. let go — i'll take it from here~",
                    ["circe"] = "i have control now.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "takeover_off",
                Aliases = new[] { "stop taking over", "stop the takeover", "you can stop now", "give me control back", "stop taking control" },
                Execute = () => App.Autonomy?.Stop(),
                VoiceRuleId = "voicecmd_takeover_off",
                Confirm = new()
                {
                    ["bambi"] = "okayy, you're back in charge~ for now hehe",
                    ["sissy"] = "control's yours again, good girl.",
                    ["circe"] = "control returned.",
                },
            },

            // ── Session control (terse) ─────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "pause",
                Aliases = new[] { "pause", "pause the session", "pause everything", "hold on" },
                Execute = () => App.MainWindowRef?.PauseSessionFromRemote(),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "paused~ take your time, cutie!",
                    ["sissy"] = "paused, good girl. breathe~",
                    ["circe"] = "paused.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "resume",
                Aliases = new[] { "resume", "resume the session", "continue", "keep going", "unpause" },
                Execute = () => App.MainWindowRef?.ResumeSessionFromRemote(),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "back to it~ yay!",
                    ["sissy"] = "good girl, let's continue~",
                    ["circe"] = "resumed.",
                },
            },

            // ── Volume / audio (terse) ──────────────────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "mute",
                // Deliberately NO bare "quiet" alias: Vosk can emit the near-homophone "quiet" when the
                // user says "quieter" (a different, non-destructive command), which would hard-mute ALL
                // audio. "be quiet" keeps the intent reachable without the one-word collision.
                Aliases = new[] { "be quiet", "mute", "silence", "shush", "hush" },
                Execute = () => App.KillAllAudio(),
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "shh~ okayy!",
                    ["sissy"] = "quiet now, good girl.",
                    ["circe"] = "silenced.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "louder",
                Aliases = new[] { "louder", "turn it up", "volume up", "more volume" },
                Execute = () => { var s = App.Settings?.Current; if (s != null) s.MasterVolume += 15; },
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "louder~ okayy!",
                    ["sissy"] = "turning it up for you, good girl~",
                    ["circe"] = "louder.",
                },
            },
            new VoiceCommandIntent
            {
                Name = "quieter",
                Aliases = new[] { "quieter", "turn it down", "volume down", "less volume", "lower the volume" },
                Execute = () => { var s = App.Settings?.Current; if (s != null) s.MasterVolume -= 15; },
                TerseAck = true,
                NoChain = true,
                Confirm = new()
                {
                    ["bambi"] = "quieter~ hehe okay!",
                    ["sissy"] = "softer now, good girl~",
                    ["circe"] = "quieter.",
                },
            },

            // ── Mic control (terse, ends the chain) ─────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "stop_listening",
                Aliases = new[] { "stop listening", "stop the mic", "turn off the mic", "you can stop listening", "mic off" },
                Execute = () => App.Autonomy?.StopVoiceInput(),
                TerseAck = true,
                NoChain = true,
                Repeatable = false,
                Confirm = new()
                {
                    ["bambi"] = "okayy, ears off~ bye bye mic!",
                    ["sissy"] = "mic off, good girl. just say my name to call me back~",
                    ["circe"] = "microphone off.",
                },
            },

            // ── "again" / "more" — repeat the last actionable command ───────────────
            new VoiceCommandIntent
            {
                Name = "replay",
                Aliases = new[] { "again", "one more", "do it again", "one more time", "more", "harder" },
                IsReplay = true,
                Repeatable = false,
                Confirm = new()
                {
                    ["bambi"] = "um... there's nothing to repeat yet~",
                    ["sissy"] = "there's nothing to repeat yet, good girl~",
                    ["circe"] = "nothing to repeat.",
                },
            },

            // ── "what can I say" help (text-only) ───────────────────────────────────
            new VoiceCommandIntent
            {
                Name = "help",
                Aliases = new[] { "what can i say", "what can you do", "help", "commands", "what can i ask for", "what are my options" },
                TerseAck = true,
                Repeatable = false,
                Confirm = new()
                {
                    ["bambi"] = "ooh lots! try: bubbles, flashes, a video, the spiral, deeper, quiz me, lock me, freeze, pause, quieter — or say red to stop everything~!",
                    ["sissy"] = "you can ask for: bubbles, flashes, a video, the spiral, deeper, a quiz, a lock card, pause, quieter — or say red and i'll stop everything, good girl~",
                    ["circe"] = "try: bubbles, flashes, video, spiral, deeper, quiz me, lock me, pause, quieter — or 'red' to stop everything.",
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

        /// <summary>The outcome of one command-listen window — drives chaining and the mantra fallback.</summary>
        private enum VoiceCmdOutcome
        {
            /// <summary>Nothing usable heard (silence / too quiet / engine unavailable).</summary>
            Silence,
            /// <summary>Heard loud speech but it matched no command (eligible for one polite re-listen).</summary>
            NoMatch,
            /// <summary>Heard an explicit "give me a mantra" request.</summary>
            Mantra,
            /// <summary>A command ran — keep a follow-up window open for chaining.</summary>
            Handled,
            /// <summary>A command ran but the chain must end now (panic / stop-listening).</summary>
            HandledFinal,
        }

        /// <summary>
        /// The serialized command driver. Listens once for a command, re-listens once on a near-miss,
        /// then (on a hit) keeps a short follow-up window open so commands can be chained. Returns true
        /// when a command was handled (caller should NOT then run a mantra); false to fall through to
        /// the mantra flow (no match, an explicit mantra request, silence, or speech unavailable).
        /// </summary>
        private async Task<bool> TryHandleVoiceCommandAsync()
        {
            var outcome = await ListenForCommandAsync().ConfigureAwait(false);

            // Heard something loud that didn't match — give one polite "say that again?" before giving up.
            if (outcome == VoiceCmdOutcome.NoMatch)
                outcome = await ListenForCommandAsync(isRetry: true).ConfigureAwait(false);

            if (outcome == VoiceCmdOutcome.Handled)
            {
                // Command chaining: stack a few quick follow-ups without re-waking. Ends on the first
                // non-command turn (silence / no-match / a final command), or after the cap.
                for (int i = 0; i < MaxChainedCommands; i++)
                {
                    var next = await ListenForCommandAsync(chained: true).ConfigureAwait(false);
                    if (next == VoiceCmdOutcome.Handled) continue;
                    // An explicit "give me a mantra" as a follow-up should still deliver a mantra, exactly
                    // like a first-turn request — fall through to the caller's mantra flow instead of being
                    // swallowed by the already-handled return.
                    if (next == VoiceCmdOutcome.Mantra) return false;
                    break; // silence / no-match / a final command ends the chain
                }
                return true;
            }
            if (outcome == VoiceCmdOutcome.HandledFinal)
                return true;

            // Mantra request, no-match (post-retry), or silence -> let the caller run the mantra flow.
            return false;
        }

        /// <summary>
        /// One command-listen window: show the listening bubble, run a grammar-constrained recognition,
        /// fuzzy-match it to an intent, and (on a hit) execute + confirm. <paramref name="chained"/> uses
        /// a shorter window and an "anything else?" prompt; <paramref name="isRetry"/> shows a "say that
        /// again?" prompt after a near-miss.
        /// </summary>
        private async Task<VoiceCmdOutcome> ListenForCommandAsync(bool chained = false, bool isRetry = false)
        {
            try
            {
                if (App.Speech?.IsAvailable != true) return VoiceCmdOutcome.Silence;

                var intents = VoiceCommandIntents;
                var grammar = intents.SelectMany(i => i.Aliases).Distinct().ToList();
                if (grammar.Count == 0) return VoiceCmdOutcome.Silence;

                // On a chained follow-up, let the previous command's confirmation bubble stay up for a
                // beat before we replace it with the "anything else?" listening dots — otherwise the
                // confirmation is overwritten within a frame and never read.
                if (chained) await Task.Delay(1400).ConfigureAwait(false);

                // Keep the speech bubble up with animated dots for the whole listen window. On the first
                // turn show the SAME line just spoken as the wake ack (handed over from OnWakeWordHeard)
                // so you read what you heard; on push-to-talk / retry / chain there's a tailored prompt.
                string? listeningLine;
                if (isRetry) listeningLine = RetryPrompt();
                else if (chained) listeningLine = ChainPrompt();
                else
                {
                    listeningLine = Interlocked.Exchange(ref _pendingWakeAckText, null);
                    if (string.IsNullOrWhiteSpace(listeningLine))
                    {
                        var wl = App.Bark?.PickVoiceLine("voicecmd_wake");
                        listeningLine = (wl is { } l && !string.IsNullOrWhiteSpace(l.Text)) ? l.Text : ListeningPrompt();
                    }
                }
                if (Application.Current?.Dispatcher != null)
                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                        { try { App.AvatarWindow?.ShowListeningBubble(listeningLine); } catch { } });

                PhraseResult res;
                try
                {
                    res = await App.Speech.RecognizeOneOfAsync(
                        grammar, new RecognizeOptions { Timeout = TimeSpan.FromSeconds(chained ? 4 : 6) }).ConfigureAwait(false);
                }
                finally
                {
                    // Drop the dots indicator. If a command matched, the confirmation bubble has already
                    // taken over (ShowGiggle clears the listening flag) so this no-ops on visibility.
                    if (Application.Current?.Dispatcher != null)
                        _ = Application.Current.Dispatcher.InvokeAsync(() =>
                            { try { App.AvatarWindow?.HideListeningBubble(); } catch { } });
                }

                if (res.Unavailable || !res.LoudEnough || string.IsNullOrWhiteSpace(res.Transcript))
                    return VoiceCmdOutcome.Silence;

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
                        "AutonomyService: voice command no-match (heard '{Heard}', best {Score:0.00})", heard, bestScore);
                    return VoiceCmdOutcome.NoMatch;
                }

                App.Logger?.Information("AutonomyService: voice command '{Name}' (heard '{Heard}', score {Score:0.00})",
                    best.Name, heard, bestScore);

                // Explicit "give me a mantra" -> let the funnel run the Spoken-Mantra flow.
                if (best.IsMantra) return VoiceCmdOutcome.Mantra;

                // "again" / "more" -> resolve to the last actionable command (if any).
                var target = best;
                if (best.IsReplay)
                {
                    target = _lastVoiceIntent;
                    if (target == null)
                    {
                        if (Application.Current?.Dispatcher != null)
                            await Application.Current.Dispatcher.InvokeAsync(() => ExecuteIntentAndConfirm(best));
                        return VoiceCmdOutcome.Handled; // acknowledged ("nothing to repeat") — still chainable
                    }
                }

                var toRun = target;
                if (Application.Current?.Dispatcher != null)
                    await Application.Current.Dispatcher.InvokeAsync(() => ExecuteIntentAndConfirm(toRun));

                // Remember the last actionable command so a later "again"/"more" can replay it.
                if (best.Repeatable) _lastVoiceIntent = best;

                return best.NoChain ? VoiceCmdOutcome.HandledFinal : VoiceCmdOutcome.Handled;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AutonomyService: ListenForCommandAsync failed");
                return VoiceCmdOutcome.Silence;
            }
        }

        /// <summary>Run an intent's action and speak its confirmation. Must be called on the UI thread.</summary>
        private static void ExecuteIntentAndConfirm(VoiceCommandIntent intent)
        {
            try { intent.Execute?.Invoke(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: voice command '{Name}' execute failed", intent.Name); }

            // Utility verbs get a short, text-only ack. Feature verbs prefer the voiced manifest variant
            // (text + matching clip for the active mod), falling back to the inline per-mod text.
            string confirm;
            string? audio = null;
            var voiced = (!intent.TerseAck && intent.VoiceRuleId != null)
                ? App.Bark?.PickVoiceLine(intent.VoiceRuleId)
                : null;
            if (voiced is { } line && !string.IsNullOrWhiteSpace(line.Text))
            {
                confirm = line.Text;
                audio = line.Audio;
            }
            else
            {
                var modKey = ModKey();
                confirm = intent.Confirm.TryGetValue(modKey, out var c) && !string.IsNullOrWhiteSpace(c)
                    ? c
                    : intent.Confirm.Values.FirstOrDefault() ?? "okay~";
            }

            try { App.AvatarWindow?.GigglePriority(confirm, playSound: audio != null, aiGenerated: false,
                phraseAudioPath: audio, barkVoice: audio != null); } catch { }
        }

        /// <summary>First-turn "I'm listening" prompt when no voiced wake line is available.</summary>
        private static string ListeningPrompt() => ModKey() switch
        {
            "bambi" => "mmm? i'm listening~",
            "circe" => "i'm listening.",
            _       => "i'm listening, good girl~",
        };

        /// <summary>Follow-up prompt shown during command chaining.</summary>
        private static string ChainPrompt() => ModKey() switch
        {
            "bambi" => "ooh, anything else?~",
            "circe" => "anything else?",
            _       => "anything else, good girl?~",
        };

        /// <summary>Prompt shown for the one polite re-listen after a near-miss.</summary>
        private static string RetryPrompt() => ModKey() switch
        {
            "bambi" => "hmm? say that again?~",
            "circe" => "again?",
            _       => "sorry love, say that again?~",
        };
    }
}
