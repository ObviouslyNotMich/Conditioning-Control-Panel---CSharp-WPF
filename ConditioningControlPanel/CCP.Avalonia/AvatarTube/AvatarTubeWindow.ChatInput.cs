using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private async Task TriggerActivityCommentAsync()
        {
            await Task.Yield();
            Giggle(GetRandomBambiPhrase());
        }

        private void ImgAvatar_MouseLeftButtonDown()
        {
            var now = DateTime.Now;
            _achievementService?.TrackAvatarClick();

            if (_circeEmoteMode && CirceClickEmote())
            {
                PlayClickBounce();
                _lastClickTime = now;
                return;
            }

            _animationRefreshClickCount++;
            if (_animationRefreshClickCount >= 4)
            {
                _animationRefreshClickCount = 0;
                RefreshAvatarAnimation();
            }

            _rapidClickTimestamps.Add(now);
            _rapidClickTimestamps.RemoveAll(t => (now - t).TotalSeconds > 60);
            if (_rapidClickTimestamps.Count >= 50)
            {
                _rapidClickTimestamps.Clear();
                TriggerBambiCumAndCollapse();
            }

            if (_random.Next(25) == 0) PlayAvatarPopSound();

            if ((now - _lastClickTime).TotalMilliseconds < 300)
            {
                if (_isMuted) ShowMutedIndicator();
                else if ((now - _lastInteractionTime).TotalSeconds >= 1.5)
                {
                    _lastInteractionTime = now;
                    _ = TriggerActivityCommentAsync();
                }
            }
            _lastClickTime = now;
            PlayClickBounce();
        }

        private bool IsDescendantOf(Visual? element, Visual? parent)
        {
            while (element != null)
            {
                if (element == parent) return true;
                element = element.GetVisualParent();
            }
            return false;
        }

        private void ForceForegroundWindow()
        {
            try { Activate(); } catch { }
            // TODO: Windows AttachThreadInput for tool-window focus; on Linux/macOS use Topmost pulse.
        }

        public void ShowEmoteFeedback(string text, bool isPending)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_isWaitingForAi || _isShowingAiBubble) return;
                var safe = (text ?? "").Trim();
                if (safe.Length > 40) safe = safe[..40] + "...";
                var content = isPending ? "Sending..." : $"Sent: \"{safe}\"";
                _speechTimer?.Stop();
                _speechDelayTimer?.Stop();
                _speechQueue.Clear();
                _isGiggling = false;
                ShowGiggle(content, playSound: false, source: SpeechSource.Preset);
                if (_speechTimer != null) _speechTimer.Interval += TimeSpan.FromSeconds(1);
            });
        }

        private static bool TryParseModifiers(string s, out KeyModifiers result)
        {
            result = KeyModifiers.None;
            if (string.IsNullOrWhiteSpace(s)) return true;
            foreach (var part in s.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse<KeyModifiers>(part, true, out var mk))
                    result |= mk;
                else
                    return false;
            }
            return true;
        }

        public static string SerializeModifiers(KeyModifiers m)
        {
            if (m == KeyModifiers.None) return "";
            var parts = new System.Collections.Generic.List<string>();
            if ((m & KeyModifiers.Control) != 0) parts.Add("Control");
            if ((m & KeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((m & KeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((m & KeyModifiers.Meta) != 0) parts.Add("Meta");
            return string.Join(",", parts);
        }
    }
}
