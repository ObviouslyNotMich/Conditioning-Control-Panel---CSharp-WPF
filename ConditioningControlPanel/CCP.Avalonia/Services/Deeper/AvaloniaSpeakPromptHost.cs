using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core;
using ConditioningControlPanel.Core.Services.Deeper;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Speech;
using ConditioningControlPanel.Models.Deeper;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Services.Deeper;

/// <summary>
/// Avalonia host for the Deeper <c>speak</c> effect. Drives a "say X for me" voice prompt: cues the
/// phrase via the companion bubble, opens the offline recognizer, fuzzy-scores the response, flashes
/// correct/incorrect feedback, and counts reps. Self-skips when mic consent isn't given or the engine
/// is unavailable.
///
/// Bounded port of the WPF SpeakPromptSession: the cue is shown via the avatar bubble rather than a
/// dedicated SpeakCueOverlay window, and the region-hold (loop/pause until satisfied via
/// IPlaybackTimeSource) is not yet wired — the prompt listens + gives feedback + counts reps.
/// </summary>
public sealed class AvaloniaSpeakPromptHost : ISpeakPromptHost
{
    private readonly ISpeechRecognitionService? _speech;
    private readonly IAvatarWindowService? _avatar;
    private readonly ISettingsService? _settings;
    private readonly ILogger<AvaloniaSpeakPromptHost>? _logger;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessions = new();

    public AvaloniaSpeakPromptHost(
        ISpeechRecognitionService? speech = null,
        IAvatarWindowService? avatar = null,
        ISettingsService? settings = null,
        ILogger<AvaloniaSpeakPromptHost>? logger = null)
    {
        _speech = speech;
        _avatar = avatar;
        _settings = settings;
        _logger = logger;
    }

    public void StartSpeak(TriggerEffectAction effect, IPlaybackTimeSource? source)
    {
        var target = (effect.SpeakTarget ?? "").Trim();
        if (string.IsNullOrWhiteSpace(target)) return;
        if (_settings?.Current?.MicConsentGiven != true) return;
        if (_speech?.IsAvailable != true) return;

        var key = string.IsNullOrEmpty(effect.EffectId) ? Guid.NewGuid().ToString("N") : effect.EffectId!;
        var cts = new CancellationTokenSource();
        if (!_sessions.TryAdd(key, cts)) { cts.Dispose(); return; } // already running for this band

        var cue = string.IsNullOrWhiteSpace(effect.SpeakCue) ? $"Say {target}" : effect.SpeakCue!.Trim();
        var correct = string.IsNullOrWhiteSpace(effect.SpeakCorrectMessage) ? "good girl" : effect.SpeakCorrectMessage!.Trim();
        var incorrect = string.IsNullOrWhiteSpace(effect.SpeakIncorrectMessage) ? "try again" : effect.SpeakIncorrectMessage!.Trim();
        var required = Math.Clamp(effect.SpeakRequiredReps, 1, 5);

        _ = Task.Run(() => RunAsync(key, target, cue, correct, incorrect, required, cts.Token));
    }

    public void StopSpeak(string? effectId)
    {
        if (string.IsNullOrEmpty(effectId))
        {
            foreach (var kv in _sessions) { try { kv.Value.Cancel(); } catch { } }
            return;
        }
        if (_sessions.TryGetValue(effectId!, out var cts))
        {
            try { cts.Cancel(); } catch { }
        }
    }

    private async Task RunAsync(string key, string target, string cue, string correct, string incorrect, int required, CancellationToken ct)
    {
        try
        {
            int reps = 0;
            while (reps < required && !ct.IsCancellationRequested)
            {
                Cue(cue, audio: null);
                var res = await _speech!.RecognizePhraseAsync(target,
                    new RecognizeOptions { Timeout = TimeSpan.FromSeconds(8), OnsetTimeout = TimeSpan.FromSeconds(4) }, ct)
                    .ConfigureAwait(false);

                if (res.Unavailable || ct.IsCancellationRequested) break;

                if (res.Matched)
                {
                    reps++;
                    Cue(correct, audio: null);
                }
                else if (!res.TimedOut)
                {
                    Cue(incorrect, audio: null);
                }
                // brief beat so feedback is readable before the next cue
                try { await Task.Delay(900, ct).ConfigureAwait(false); } catch { break; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger?.LogDebug(ex, "AvaloniaSpeakPromptHost: speak session failed"); }
        finally
        {
            if (_sessions.TryRemove(key, out var cts)) { try { cts.Dispose(); } catch { } }
        }
    }

    private void Cue(string text, string? audio)
    {
        try { _avatar?.GigglePriority(text, playSound: audio != null, aiGenerated: false, phraseAudioPath: audio, barkVoice: audio != null); }
        catch { }
    }
}
