using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Core.Services.Speech;

/// <summary>
/// One voice-command intent = a set of spoken aliases mapped to a stable <see cref="Name"/>.
/// This is the PORTABLE half of the "Hey Bambi" command layer: the grammar (what can be said) and
/// the routing flags. The per-head dispatch maps <see cref="Name"/> to an action and pulls the voiced
/// confirmation line from the bark manifest by <see cref="VoiceRuleId"/>. Ported verbatim from the WPF
/// AutonomyService.VoiceCommands grammar so phrasings are identical.
/// </summary>
public sealed class VoiceCommandSpec
{
    public string Name { get; init; } = "";
    public string[] Aliases { get; init; } = Array.Empty<string>();
    /// <summary>No <see cref="Name"/> action — fall back to the spoken-mantra flow.</summary>
    public bool IsMantra { get; init; }
    /// <summary>"again"/"more"/"harder" — re-run the last actionable command instead of a fixed action.</summary>
    public bool IsReplay { get; init; }
    /// <summary>Don't open a follow-up (chaining) window after this one runs — e.g. panic, stop-listening.</summary>
    public bool NoChain { get; init; }
    /// <summary>Eligible to be the target of a later "again"/"more". False for help, replay, panic, stop-listening.</summary>
    public bool Repeatable { get; init; } = true;
    /// <summary>Short text-only confirmation (utility verbs) instead of a full voiced bark.</summary>
    public bool TerseAck { get; init; }
    /// <summary>Bark-manifest rule id whose pool holds this command's voiced confirmations. Null = text-only.</summary>
    public string? VoiceRuleId { get; init; }
}

/// <summary>
/// The portable "Hey Bambi" command grammar + fuzzy intent router. The recognizer is grammar-constrained
/// to these aliases (Vosk can only emit alias words or [unk]); this router then picks the best intent with
/// the same word-level <see cref="SpeechMatching.Similarity"/> the mantra mechanic uses. Pure logic — no
/// engine or platform dependency — so it is unit-testable and shared by every head's dispatch.
/// </summary>
public static class VoiceCommandGrammar
{
    /// <summary>
    /// Minimum fuzzy similarity. Word-level (1 - wordEditDistance/maxWords), so each dropped/wrong word
    /// costs 1/maxWords. 0.5 = "at least half the words align", which recovers single-noun and one-word-off
    /// utterances. Safe to relax because recognition is grammar-constrained; don't go below 0.5.
    /// </summary>
    public const double MatchThreshold = 0.5;

    /// <summary>After a hit, keep listening for a few quick follow-ups (no wake word) so commands can be stacked.</summary>
    public const int MaxChainedCommands = 3;

    // Toggle features share a wide, consistent spoken vocabulary so natural phrasings all land.
    // `bare` = the noun without an article ("bubbles"); `the` = with an article ("the bubbles").
    private static string[] OnAliases(string bare, string the, params string[] extra)
    {
        var list = new List<string>
        {
            $"{bare} on", $"turn on {the}", $"turn {the} on", $"start {the}", $"start {bare}",
            $"begin {the}", $"show me {bare}", $"show me {the}", $"give me {bare}", $"give me {the}",
            $"i want {bare}", $"bring up {the}", $"enable {bare}",
        };
        list.AddRange(extra);
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] OffAliases(string bare, string the, params string[] extra)
    {
        var list = new List<string>
        {
            $"{bare} off", $"turn off {the}", $"turn {the} off", $"stop {the}", $"stop {bare}",
            $"end {the}", $"no more {bare}", $"hide {the}", $"get rid of {the}", $"kill {the}",
            $"cut {the}", $"enough {bare}", $"disable {bare}",
        };
        list.AddRange(extra);
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static List<VoiceCommandSpec>? _intents;

    /// <summary>The full command set (verbatim from WPF). Built lazily.</summary>
    public static IReadOnlyList<VoiceCommandSpec> Intents => _intents ??= new()
    {
        // ── Safety ──
        new() { Name = "panic", VoiceRuleId = "voicecmd_panic", NoChain = true, Repeatable = false,
            Aliases = new[] { "red", "stop everything", "stop it all", "shut it all down", "safe word", "i'm done", "make it all stop", "make it stop", "everything off", "turn everything off", "kill everything", "that's too much", "i need to stop", "all stop", "emergency stop" } },

        // ── Bubbles ──
        new() { Name = "bubbles_on", VoiceRuleId = "voicecmd_bubbles_on", Aliases = OnAliases("bubbles", "the bubbles", "show me some bubbles", "more bubbles") },
        new() { Name = "bubbles_off", VoiceRuleId = "voicecmd_bubbles_off", Aliases = OffAliases("bubbles", "the bubbles") },

        // ── Video ──
        new() { Name = "video_on", VoiceRuleId = "voicecmd_video_on", Aliases = new[] { "show me a video", "play a video", "play a hypnotube video", "play hypnotube", "give me a video", "i want a video", "put on a video", "play me a video", "start a video", "play video", "video on", "show me a clip" } },
        new() { Name = "video_off", VoiceRuleId = "voicecmd_video_off", Aliases = OffAliases("video", "the video", "no more videos", "stop playing") },
        new() { Name = "video_pause", TerseAck = true, NoChain = true, Aliases = new[] { "pause the video", "pause video", "pause this video", "pause the clip", "hold the video" } },
        new() { Name = "video_resume", TerseAck = true, NoChain = true, Aliases = new[] { "resume the video", "play the video", "unpause the video", "continue the video", "resume video", "play the clip", "keep playing", "continue playing" } },

        // ── Flashes (one-shot) ──
        new() { Name = "flash_once", VoiceRuleId = "voicecmd_flash_once", Aliases = new[] { "flash me", "one flash", "give me a flash", "flash once", "just one flash", "quick flash", "a single flash", "one quick flash" } },

        // ── Subliminals ──
        new() { Name = "subliminals_on", VoiceRuleId = "voicecmd_subliminals_on", Aliases = OnAliases("subliminals", "the subliminals") },
        new() { Name = "subliminals_off", VoiceRuleId = "voicecmd_subliminals_off", Aliases = OffAliases("subliminals", "the subliminals") },

        // ── Bouncing text ──
        new() { Name = "bouncing_on", VoiceRuleId = "voicecmd_bouncing_on", Aliases = OnAliases("bouncing text", "the bouncing text") },
        new() { Name = "bouncing_off", VoiceRuleId = "voicecmd_bouncing_off", Aliases = OffAliases("bouncing text", "the bouncing text") },

        // ── Spiral ──
        new() { Name = "spiral_on", VoiceRuleId = "voicecmd_spiral_on", Aliases = OnAliases("spiral", "the spiral") },
        new() { Name = "spiral_off", VoiceRuleId = "voicecmd_spiral_off", Aliases = OffAliases("spiral", "the spiral") },

        // ── Pink filter ──
        new() { Name = "pink_on", VoiceRuleId = "voicecmd_pink_on", Aliases = OnAliases("pink filter", "the pink filter", "make it pink", "go pink") },
        new() { Name = "pink_off", VoiceRuleId = "voicecmd_pink_off", Aliases = OffAliases("pink filter", "the pink filter", "make it normal") },

        // ── Mind wipe (one-shot) ──
        new() { Name = "wipe_once", VoiceRuleId = "voicecmd_wipe_once", Aliases = new[] { "wipe my mind", "wipe me", "empty my head", "blank my mind", "wipe my brain", "clear my mind", "empty my mind", "erase my thoughts" } },

        // ── Lock cards (one-shot) ──
        new() { Name = "lock_once", VoiceRuleId = "voicecmd_lock_once", Aliases = new[] { "lock me", "lock card now", "give me a lock card", "show me a lock card", "a lock card", "one lock card", "lock me up", "lock me down" } },

        // ── Pop quiz (one-shot) ──
        new() { Name = "quiz_once", VoiceRuleId = "voicecmd_quiz_once", Aliases = new[] { "quiz me", "quiz me now", "give me a quiz", "test me", "pop quiz", "quiz time", "ask me a question", "give me a question" } },

        // ── Keyword triggers ──
        new() { Name = "keyword_on", VoiceRuleId = "voicecmd_keyword_on", Aliases = OnAliases("keyword triggers", "the keyword triggers", "trigger words on") },
        new() { Name = "keyword_off", VoiceRuleId = "voicecmd_keyword_off", Aliases = OffAliases("keyword triggers", "the keyword triggers", "trigger words off") },

        // ── One-shot toys ──
        new() { Name = "count_once", VoiceRuleId = "voicecmd_count_once", Aliases = new[] { "count for me", "count the bubbles", "give me a counting game", "make me count", "let me count", "counting game", "time to count", "i want to count" } },
        new() { Name = "freeze_once", VoiceRuleId = "voicecmd_freeze_once", Aliases = new[] { "freeze", "freeze me", "freeze for me", "bambi freeze", "freeze now", "hold still", "stay still", "don't move" } },
        new() { Name = "shake_once", VoiceRuleId = "voicecmd_shake_once", Aliases = new[] { "shake the screen", "shake it", "shake me", "earthquake", "shake things up", "make it shake", "shake everything", "shake the room" } },

        // ── Deeper ──
        new() { Name = "deeper", VoiceRuleId = "voicecmd_deeper", Aliases = new[] { "deeper", "go deeper", "take me deeper", "sink deeper", "drop deeper", "deeper now", "further down", "take me down", "drop me down", "make me go deeper", "i want to go deeper" } },

        // ── Takeover ──
        new() { Name = "takeover_on", VoiceRuleId = "voicecmd_takeover_on", Aliases = new[] { "take over", "take control", "you're in charge", "take over for me", "you take over", "take the wheel", "you drive", "you're in control", "control me", "i give up control" } },
        new() { Name = "takeover_off", VoiceRuleId = "voicecmd_takeover_off", Aliases = new[] { "stop taking over", "stop the takeover", "you can stop now", "give me control back", "stop taking control", "i want control back", "let me drive", "give me back control", "take over off", "release control" } },

        // ── Session control (terse) ──
        new() { Name = "pause", TerseAck = true, NoChain = true, Aliases = new[] { "pause", "pause the session", "pause everything", "hold on", "pause please", "pause it", "wait", "hold up", "take a break", "one moment", "give me a moment" } },
        new() { Name = "resume", TerseAck = true, NoChain = true, Aliases = new[] { "resume", "resume the session", "continue", "keep going", "unpause", "carry on", "go on", "let's continue", "back to it", "resume please", "continue the session" } },

        // ── Volume / audio (terse) ──
        new() { Name = "mute", TerseAck = true, NoChain = true, Aliases = new[] { "be quiet", "mute", "silence", "shush", "hush", "mute it", "mute everything", "quiet please", "stop the sound", "stop the audio", "no sound", "kill the sound", "turn off the sound" } },
        new() { Name = "unmute", TerseAck = true, NoChain = true, Aliases = new[] { "unmute", "un mute", "unmute yourself", "unmute everything", "sound on", "audio on", "turn the sound on", "turn the sound back on", "turn sound back on", "you can talk again", "i can't hear you" } },
        new() { Name = "louder", TerseAck = true, NoChain = true, Aliases = new[] { "louder", "turn it up", "volume up", "more volume", "turn up the volume", "crank it up", "louder please", "make it louder", "raise the volume", "pump it up" } },
        new() { Name = "quieter", TerseAck = true, NoChain = true, Aliases = new[] { "quieter", "turn it down", "volume down", "less volume", "lower the volume", "turn down the volume", "quieter please", "make it quieter", "not so loud", "softer", "tone it down" } },

        // ── Mic control (terse, ends the chain) ──
        new() { Name = "stop_listening", TerseAck = true, NoChain = true, Repeatable = false, Aliases = new[] { "stop listening", "stop the mic", "turn off the mic", "you can stop listening", "mic off", "stop the microphone", "turn off the microphone", "microphone off", "stop hearing me", "close the mic", "mute the mic" } },

        // ── "again" / "more" ──
        new() { Name = "replay", IsReplay = true, Repeatable = false, Aliases = new[] { "again", "one more", "do it again", "one more time", "more", "harder", "repeat", "repeat that", "do that again", "once more", "encore" } },

        // ── help (text-only) ──
        new() { Name = "help", TerseAck = true, Repeatable = false, NoChain = true, Aliases = new[] { "what can i say", "what can you do", "help", "commands", "what can i ask for", "what are my options", "what are the commands", "what should i say", "list commands", "what do you understand" } },

        // ── Explicit mantra request — defer to the spoken-mantra flow ──
        new() { Name = "mantra", IsMantra = true, Aliases = new[] { "give me a mantra", "make me say it", "i want a mantra", "let me say something", "say a mantra", "mantra please", "give me something to say", "let me speak" } },
    };

    /// <summary>Every alias across every intent — the grammar handed to the recognizer.</summary>
    public static IReadOnlyList<string> AllAliases() => Intents.SelectMany(i => i.Aliases).Distinct().ToList();

    /// <summary>
    /// Fuzzy-match a heard transcript to a command intent. Scores the transcript against every alias of
    /// every intent (best alias wins) and returns the highest-scoring intent at or above
    /// <paramref name="threshold"/>, else null. Identical scoring to the WPF matcher.
    /// </summary>
    public static VoiceCommandSpec? Match(string? transcript, double threshold = MatchThreshold)
    {
        var heard = SpeechMatching.Normalize(transcript);
        if (heard.Length == 0) return null;

        VoiceCommandSpec? best = null;
        double bestScore = 0;
        foreach (var intent in Intents)
        {
            foreach (var alias in intent.Aliases)
            {
                var score = SpeechMatching.Similarity(SpeechMatching.Normalize(alias), heard);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = intent;
                }
            }
        }
        return bestScore >= threshold ? best : null;
    }
}
