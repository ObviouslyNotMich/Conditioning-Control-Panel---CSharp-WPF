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

        /// <summary>Update the status strip: mic readiness + whether Takeover is running.</summary>
        private void RefreshSheListeningStatus()
        {
            if (SheListeningTab?.SL_StatusTitle == null) return;

            var available = App.Speech?.IsAvailable == true;
            var running = App.Autonomy?.IsEnabled == true;

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

            if (running)
            {
                SheListeningTab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));
                SheListeningTab.SL_StatusTitle.Text = "She's listening";
                SheListeningTab.SL_StatusSub.Text = "Takeover is running — call her, then say a command.";
            }
            else
            {
                SheListeningTab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
                SheListeningTab.SL_StatusTitle.Text = "Ready — start Takeover";
                SheListeningTab.SL_StatusSub.Text = "Voice commands and the wake word listen while Takeover is running.";
            }
        }
    }
}
