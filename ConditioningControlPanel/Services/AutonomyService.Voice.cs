using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Opt-in user-driven mic input for the Takeover "say it for me" mechanic:
    ///  - <b>Wake-word</b> ("Hey Bambi"): an always-on listen loop that fires a voice prompt
    ///    when she hears her name. Runs only while Takeover is active.
    ///  - <b>Push-to-talk</b>: a system-wide key that summons a voice prompt on demand.
    ///
    /// Both are additive and self-protecting: nothing arms unless the user gave mic consent, the
    /// Vosk engine is actually available, and Takeover is running. Everything funnels through the
    /// single serialized <see cref="RequestVoiceCommand"/> so the recognizer's one-session-at-a-time
    /// guard is never violated. When either mode is on, the surprise auto-trigger is suppressed
    /// (the mic only opens on the user's initiative).
    /// </summary>
    public partial class AutonomyService
    {
        // Set for the whole life of a voice prompt (announce beat + listen + verdict) so the wake
        // loop, the PTT key, and the auto-scheduler all stand off the mic while one is in flight.
        // Claimed atomically (0/1) in case wake-word and push-to-talk ever race for it.
        private int _voiceBusyFlag;
        private bool _voiceBusy => Volatile.Read(ref _voiceBusyFlag) != 0;

        private CancellationTokenSource? _wakeLoopCts; // ends the whole wake loop
        private CancellationTokenSource? _wakeWaitCts; // cancels only the current WaitForWakeWord
        private Task? _wakeLoopTask;

        private GlobalKeyboardHook? _pttHook;

        /// <summary>Whether the user has armed a self-initiated mic mode (and so the mic only opens on demand).</summary>
        public bool UserDrivenVoiceArmed
        {
            get
            {
                var s = App.Settings?.Current;
                return s != null && (s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);
            }
        }

        /// <summary>
        /// Reconcile the wake-word loop and push-to-talk hook with current settings. Safe to call
        /// any time (lifecycle start, or when the user flips a toggle). No-op unless Takeover is
        /// running, mic consent is given, and the speech engine is available.
        /// </summary>
        public void RefreshVoiceInputModes()
        {
            try
            {
                // NOTE: deliberately NOT gated on _isEnabled (Takeover running). The mic features
                // (wake word / push-to-talk / voice commands) are decoupled from Takeover — they
                // arm from their own toggles + consent + an available engine, so "Hey Bambi" works
                // even with Takeover off. Their home UI is the "She's Listening" Exclusive.
                var s = App.Settings?.Current;
                bool baseOk = !_disposed
                              && s?.MicConsentGiven == true
                              && App.Speech?.IsAvailable == true;

                // Wake-word loop
                if (baseOk && s!.SpeechWakeWordEnabled && WakeWords().Count > 0)
                    StartWakeLoop();
                else
                    StopWakeLoop();

                // Push-to-talk hook
                if (baseOk && s!.SpeechPushToTalkEnabled)
                    StartPushToTalk();
                else
                    StopPushToTalk();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: RefreshVoiceInputModes failed"); }
        }

        /// <summary>Tear down both mic modes. Called from Stop()/Dispose().</summary>
        private void StopVoiceInputModes()
        {
            StopWakeLoop();
            StopPushToTalk();
        }

        /// <summary>
        /// User-initiated "stop the mic" (the privacy pill). Cuts any in-flight capture and tears
        /// down the wake-word loop and push-to-talk hook so the mic won't reopen until re-armed.
        /// Leaves the rest of Takeover running — this is about the microphone, not the takeover.
        /// </summary>
        public void StopVoiceInput()
        {
            try
            {
                App.Speech?.StopListening();
                StopVoiceInputModes();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: StopVoiceInput failed"); }
        }

        // ── Serialized voice-prompt entry point ───────────────────────────────

        /// <summary>
        /// The single funnel every voice initiator uses. Claims the mic, frees the wake loop if it
        /// was holding it, waits for the capture session to release, then runs one prompt. Re-entrant
        /// calls are dropped while one is already in flight.
        ///
        /// <paramref name="allowCommands"/>: user-initiated paths (wake-word / push-to-talk) first
        /// listen for a "Hey Bambi" voice command and only fall back to a mantra if none is heard;
        /// the auto-scheduler and dev test pass false so they always deliver a mantra.
        /// </summary>
        private async void RequestVoiceCommand(bool allowCommands = false)
        {
            if (App.Speech?.IsAvailable != true || App.AvatarWindow == null) return;
            // Atomically claim the mic; bail if a prompt is already in flight.
            if (Interlocked.CompareExchange(ref _voiceBusyFlag, 1, 0) != 0) return;
            try
            {
                // If the wake loop currently owns the mic, cancel its wait so the session releases.
                _wakeWaitCts?.Cancel();
                for (int i = 0; i < 24 && App.Speech?.IsListening == true; i++)
                    await Task.Delay(25).ConfigureAwait(false);

                // Voice-command layer first (only on user-initiated paths). On a match we're done; on
                // no-match the wake/PTT turn falls back to a mantra ONLY if on-demand mantras are on.
                if (allowCommands)
                {
                    if (await TryHandleVoiceCommandAsync().ConfigureAwait(false))
                        return;
                    if (App.Settings?.Current?.SpokenMantrasEnabled != true)
                        return; // commands only; no mantra fallback when on-demand mantras are off
                }

                await RunSpokenMantraAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: RequestVoiceCommand failed"); }
            finally { Volatile.Write(ref _voiceBusyFlag, 0); }
        }

        // ── Wake-word loop ────────────────────────────────────────────────────

        private List<string> WakeWords()
        {
            var raw = App.Settings?.Current?.SpeechWakeWords ?? "";
            return raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(w => w.Trim())
                      .Where(w => w.Length > 0)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList();
        }

        private void StartWakeLoop()
        {
            if (_wakeLoopTask is { IsCompleted: false }) return; // already running
            _wakeLoopCts = new CancellationTokenSource();
            var ct = _wakeLoopCts.Token;
            App.Logger?.Information("AutonomyService: wake-word loop starting (words: {Words})", string.Join(" / ", WakeWords()));
            _wakeLoopTask = Task.Run(() => WakeLoopAsync(ct), ct);
        }

        private void StopWakeLoop()
        {
            try
            {
                _wakeWaitCts?.Cancel();
                _wakeLoopCts?.Cancel();
            }
            catch { }
            _wakeLoopCts = null;
            _wakeLoopTask = null; // the loop observes the token and unwinds on its own
        }

        private async Task WakeLoopAsync(CancellationToken loopCt)
        {
            try
            {
                while (!loopCt.IsCancellationRequested)
                {
                    // Stand off the mic while a prompt is in flight or another session holds it.
                    if (_voiceBusy || App.Speech?.IsListening == true || App.Speech?.IsAvailable != true)
                    {
                        await Task.Delay(350, loopCt).ConfigureAwait(false);
                        continue;
                    }

                    var words = WakeWords();
                    if (words.Count == 0)
                    {
                        await Task.Delay(500, loopCt).ConfigureAwait(false);
                        continue;
                    }

                    string? heard = null;
                    using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt))
                    {
                        _wakeWaitCts = waitCts;
                        try
                        {
                            heard = await App.Speech!.WaitForWakeWordAsync(words, waitCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { /* normal on stop/interrupt */ }
                        catch (Exception ex)
                        {
                            App.Logger?.Warning(ex, "AutonomyService: wake-word wait failed");
                            await Task.Delay(800, loopCt).ConfigureAwait(false);
                        }
                        finally { _wakeWaitCts = null; }
                    }

                    if (loopCt.IsCancellationRequested) break;
                    if (!string.IsNullOrWhiteSpace(heard))
                    {
                        App.Logger?.Information("AutonomyService: wake word heard ('{Heard}')", heard);
                        OnWakeWordHeard();
                        // Let the prompt claim the mic before we loop back and re-grab it.
                        await Task.Delay(400, loopCt).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { /* clean shutdown */ }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: wake-word loop crashed"); }
            App.Logger?.Information("AutonomyService: wake-word loop ended");
        }

        private void OnWakeWordHeard()
        {
            DispatcherHelper.RunOnUI(() =>
            {
                try
                {
                    // Per-mod wake acknowledgement: voiced manifest variant when available (rotates,
                    // avoiding immediate repeats), else a plain per-mod line, text-only.
                    var voiced = App.Bark?.PickVoiceLine("voicecmd_wake");
                    string ack;
                    string? audio = null;
                    if (voiced is { } line && !string.IsNullOrWhiteSpace(line.Text))
                    {
                        ack = line.Text;
                        audio = line.Audio;
                    }
                    else
                    {
                        ack = ModKey() switch
                        {
                            "bambi" => "mmm? you called for me~",
                            "circe" => "you called. i'm listening.",
                            _       => "yes, lovely? i'm right here~",
                        };
                    }
                    App.AvatarWindow?.GigglePriority(ack, playSound: audio != null, aiGenerated: false,
                        phraseAudioPath: audio, barkVoice: audio != null);
                }
                catch { }
            });
            RequestVoiceCommand(allowCommands: true);
        }

        // ── Push-to-talk ──────────────────────────────────────────────────────

        /// <summary>The configured push-to-talk key, or F8 if the saved value won't parse.</summary>
        public Key PushToTalkKey()
        {
            var raw = App.Settings?.Current?.SpeechPushToTalkKey;
            if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse<Key>(raw, ignoreCase: true, out var k) && k != Key.None)
                return k;
            return Key.F8;
        }

        private void StartPushToTalk()
        {
            if (_pttHook != null) return;
            try
            {
                _pttHook = new GlobalKeyboardHook();
                _pttHook.KeyPressed += OnPushToTalkKey;
                _pttHook.Start();
                App.Logger?.Information("AutonomyService: push-to-talk armed on key {Key}", PushToTalkKey());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AutonomyService: failed to arm push-to-talk hook");
                _pttHook = null;
            }
        }

        private void StopPushToTalk()
        {
            if (_pttHook == null) return;
            try
            {
                _pttHook.KeyPressed -= OnPushToTalkKey;
                _pttHook.Dispose();
            }
            catch { }
            _pttHook = null;
        }

        private void OnPushToTalkKey(Key key)
        {
            if (key != PushToTalkKey()) return;
            if (_voiceBusy) return;
            if (!_isEnabled || App.Speech?.IsAvailable != true) return;
            App.Logger?.Information("AutonomyService: push-to-talk pressed");
            // OnPushToTalkKey runs on the low-level hook thread, which must return fast — marshal
            // to the UI thread without blocking the hook.
            DispatcherHelper.RunOnUI(() => RequestVoiceCommand(allowCommands: true));
        }
    }
}
