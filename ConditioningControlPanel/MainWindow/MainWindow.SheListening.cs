using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "She's Listening" Exclusive — the voice-control surface. Its toggles drive the SAME
    /// AppSettings as the Takeover tab, so rather than duplicate the consent/settings logic, these
    /// handlers mirror the value onto the Takeover control, call the existing handler (which runs
    /// consent + saves + RefreshVoiceInputModes), then reflect the result back. Both tabs stay in
    /// sync, and there is one source of truth for the logic.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Copy a checkbox's value to another without firing its Changed handler.</summary>
        private void MirrorCheck(CheckBox from, CheckBox to)
        {
            var wasLoading = _isLoading;
            _isLoading = true;
            to.IsChecked = from.IsChecked;
            _isLoading = wasLoading;
        }

        /// <summary>
        /// On-demand spoken mantras (the She's Listening capability = AppSettings.SpokenMantrasEnabled).
        /// Separate from the Takeover "surprise" auto-trigger (AutonomyCanTriggerVoiceCommand). First
        /// enable asks for mic consent, since a mantra opens the mic to hear you repeat the phrase.
        /// </summary>
        internal void SL_Mantras_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || SheListeningTab == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var turningOn = SheListeningTab.ChkSL_Mantras.IsChecked == true;
            if (turningOn && !s.MicConsentGiven)
            {
                var dlg = new MicConsentDialog { Owner = this };
                var ok = dlg.ShowDialog() == true && dlg.ConsentGiven;
                if (!ok)
                {
                    var wasLoading = _isLoading;
                    _isLoading = true;
                    SheListeningTab.ChkSL_Mantras.IsChecked = false;
                    _isLoading = wasLoading;
                    return;
                }
            }

            s.SpokenMantrasEnabled = turningOn;
            App.Settings?.Save();
            RefreshSheListeningStatus();
        }

        internal void SL_WakeWord_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || SheListeningTab == null) return;
            MirrorCheck(SheListeningTab.ChkSL_WakeWord, BambiTakeoverTab.ChkSpeechWakeWord);
            ChkSpeechWakeWord_Changed(sender, e);
            MirrorCheck(BambiTakeoverTab.ChkSpeechWakeWord, SheListeningTab.ChkSL_WakeWord);
            RefreshSheListeningStatus();
            RefreshPremiumRail();
        }

        internal void SL_WakeWords_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading || SheListeningTab == null) return;
            var wasLoading = _isLoading;
            _isLoading = true;
            BambiTakeoverTab.TxtSpeechWakeWords.Text = SheListeningTab.TxtSL_WakeWords.Text;
            _isLoading = wasLoading;
            TxtSpeechWakeWords_LostFocus(sender, e);
            // The handler may normalize an empty box back to "hey bambi" — reflect that.
            _isLoading = true;
            SheListeningTab.TxtSL_WakeWords.Text = BambiTakeoverTab.TxtSpeechWakeWords.Text;
            _isLoading = wasLoading;
        }

        internal void SL_PushToTalk_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || SheListeningTab == null) return;
            MirrorCheck(SheListeningTab.ChkSL_PushToTalk, BambiTakeoverTab.ChkSpeechPushToTalk);
            ChkSpeechPushToTalk_Changed(sender, e);
            MirrorCheck(BambiTakeoverTab.ChkSpeechPushToTalk, SheListeningTab.ChkSL_PushToTalk);
            RefreshSheListeningStatus();
            RefreshPremiumRail();
        }

        /// <summary>
        /// True when the offline mic is actually armed: consent given AND at least one input mode
        /// (wake word or push-to-talk) is on. This is the "She's Listening" master on/off state —
        /// fully independent of Takeover.
        /// </summary>
        internal bool MicIsArmed()
        {
            var s = App.Settings?.Current;
            return s != null && s.MicConsentGiven
                   && (s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);
        }

        /// <summary>
        /// Master mic switch for She's Listening (and the dashboard Voice chip). Off→On: consent,
        /// then enable the wake word by default (so she actually listens) and arm. On→Off: disable
        /// both input modes and cut any in-flight capture. Independent of Takeover.
        /// </summary>
        internal void ToggleVoiceMic()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            if (MicIsArmed()) { DisarmVoiceMic(); return; }

            // Arm: consent, then default to the wake word so "she's listening" means something.
            if (App.Speech?.IsAvailable != true)
            {
                MessageBox.Show(this,
                    Services.Speech.SpeechService.HasCaptureDevice
                        ? "The offline speech model isn't installed yet, so the mic can't start."
                        : "No microphone detected — connect one to use voice control.",
                    "She's Listening", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!s.MicConsentGiven)
            {
                var dlg = new MicConsentDialog { Owner = this };
                if (!(dlg.ShowDialog() == true && dlg.ConsentGiven)) return;
            }
            if (!s.SpeechWakeWordEnabled && !s.SpeechPushToTalkEnabled)
                s.SpeechWakeWordEnabled = true;
            App.Settings?.Save();
            App.Autonomy?.RefreshVoiceInputModes();

            RefreshSheListeningTab(); // reload the sub-toggles + status
            RefreshPremiumRail();     // keep the dashboard Voice dot honest
        }

        /// <summary>
        /// Fully turn the offline mic OFF and keep it off: clear the continuous input modes (wake word
        /// + push-to-talk) so nothing re-arms it, cut any in-flight capture, then repaint the dashboard
        /// dot + She's Listening. Shared by the master Stop button and the title-bar privacy pill so
        /// both genuinely disarm (not just pause) and the UI stays honest.
        /// </summary>
        internal void DisarmVoiceMic()
        {
            var s = App.Settings?.Current;
            if (s != null)
            {
                s.SpeechWakeWordEnabled = false;
                s.SpeechPushToTalkEnabled = false;
                App.Settings?.Save();
            }
            try { App.Speech?.StopListening(); } catch { }
            try { App.Autonomy?.StopVoiceInput(); } catch { }

            if (SheListeningTab != null) RefreshSheListeningTab();
            RefreshPremiumRail();
        }

        internal void SL_SetPttKey_Click(object sender, RoutedEventArgs e)
        {
            if (_capturingPttKey || SheListeningTab == null) return;
            _capturingPttKey = true;
            SheListeningTab.BtnSL_SetPttKey.Content = "Press a key…";
            PreviewKeyDown += CaptureSlPttKey;
        }

        private void CaptureSlPttKey(object sender, KeyEventArgs e)
        {
            if (!_capturingPttKey) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
                return;

            e.Handled = true;
            _capturingPttKey = false;
            PreviewKeyDown -= CaptureSlPttKey;

            var s = App.Settings?.Current;
            if (s != null)
            {
                s.SpeechPushToTalkKey = key.ToString();
                App.Settings?.Save();
                App.Autonomy?.RefreshVoiceInputModes();
            }
            var keyStr = key.ToString();
            if (SheListeningTab != null)
            {
                SheListeningTab.TxtSL_PttKey.Text = keyStr;
                SheListeningTab.BtnSL_SetPttKey.Content = "Set key…";
            }
            // Keep the Takeover tab's label in sync too.
            if (BambiTakeoverTab?.TxtPttKey != null) BambiTakeoverTab.TxtPttKey.Text = keyStr;
        }

        /// <summary>Load the She's-Listening controls from settings + refresh status/gate. Called on tab show.</summary>
        internal void RefreshSheListeningTab()
        {
            if (SheListeningTab == null) return;
            var s = App.Settings?.Current;
            var wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                if (s != null)
                {
                    SheListeningTab.ChkSL_Mantras.IsChecked = s.SpokenMantrasEnabled && s.MicConsentGiven;
                    SheListeningTab.ChkSL_WakeWord.IsChecked = s.SpeechWakeWordEnabled && s.MicConsentGiven;
                    SheListeningTab.TxtSL_WakeWords.Text = string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords;
                    SheListeningTab.ChkSL_PushToTalk.IsChecked = s.SpeechPushToTalkEnabled && s.MicConsentGiven;
                    SheListeningTab.TxtSL_PttKey.Text = string.IsNullOrWhiteSpace(s.SpeechPushToTalkKey) ? "F8" : s.SpeechPushToTalkKey;
                }
            }
            finally { _isLoading = wasLoading; }

            RefreshSheListeningStatus();
            RefreshPremiumGate(SheListeningTab.SheListeningGate);
        }

        /// <summary>Update the hero: mic readiness / armed state + the master Start/Stop button.</summary>
        private void RefreshSheListeningStatus()
        {
            if (SheListeningTab?.SL_StatusTitle == null) return;

            var available = App.Speech?.IsAvailable == true;
            var armed = available && MicIsArmed();

            if (SheListeningTab.BtnSL_MicMaster != null)
            {
                SheListeningTab.BtnSL_MicMaster.IsEnabled = available;
                SheListeningTab.BtnSL_MicMaster.Content = armed ? "■  Stop listening" : "▶  Start listening";
                SheListeningTab.BtnSL_MicMaster.Foreground = armed
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0xB0))
                    : new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));
            }

            if (!available)
            {
                SheListeningTab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x5A, 0x4A, 0x6A));
                SheListeningTab.SL_StatusTitle.Text = "Microphone not ready";
                SheListeningTab.SL_StatusSub.Text =
                    App.Speech == null || !Services.Speech.SpeechService.HasCaptureDevice
                        ? "No microphone detected — connect one to use voice."
                        : "Offline speech model not installed yet — voice stays off until it is.";
                return;
            }

            if (armed)
            {
                SheListeningTab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));
                SheListeningTab.SL_StatusTitle.Text = "She's listening";
                SheListeningTab.SL_StatusSub.Text = "The mic is open. Call her, then say a command.";
            }
            else
            {
                SheListeningTab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
                SheListeningTab.SL_StatusTitle.Text = "Mic off";
                SheListeningTab.SL_StatusSub.Text = "Tap Start listening so she can hear you. Works with or without Takeover.";
            }
        }
    }
}
