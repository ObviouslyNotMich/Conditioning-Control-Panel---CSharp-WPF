using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.AIService;
using Microsoft.Extensions.DependencyInjection;

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

            // Bark hook: mirror WPF's App.Bark?.NotifyAvatarClicked().
            try { _barkService?.NotifyAvatarClicked(); } catch { }

            // Animated avatar: a click rotates to a rare affectionate emote (3s cooldown). No-op for
            // static/portrait avatars or while cooling down.
            try { CirceClickEmote(); } catch { }

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

            // Double-click detection — open chat input if AI available, otherwise activity comment.
            if ((now - _lastClickTime).TotalMilliseconds < 300)
            {
                if (_isMuted)
                {
                    ShowMutedIndicator();
                }
                else if (_settings?.Current?.AiChatEnabled == true
                         && App.Services?.GetService<IAiService>() is { IsAvailable: true })
                {
                    ShowInputPanel();
                }
                else if (_isGiggling || _isWaitingForAi)
                {
                    _logger?.Debug("Skipping double-click - message still showing");
                }
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
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                    if (handle != IntPtr.Zero)
                    {
                        var fgWindow = GetForegroundWindow();
                        var fgThread = GetWindowThreadProcessId(fgWindow, out _);
                        var currentThread = GetCurrentThreadId();
                        if (fgThread != 0 && fgThread != currentThread)
                        {
                            AttachThreadInput(currentThread, fgThread, true);
                            try
                            {
                                SetForegroundWindow(handle);
                                BringWindowToTop(handle);
                            }
                            finally
                            {
                                AttachThreadInput(currentThread, fgThread, false);
                            }
                        }
                        else
                        {
                            SetForegroundWindow(handle);
                            BringWindowToTop(handle);
                        }
                    }
                    else
                    {
                        Activate();
                    }
                }
                else
                {
                    // Linux/macOS fallback: briefly pulse Topmost to steal focus,
                    // then restore the previous value.
                    var saved = Topmost;
                    Topmost = true;
                    Activate();
                    Dispatcher.UIThread.Post(() => Topmost = saved, DispatcherPriority.Background);
                }
            }
            catch
            {
                // Fallback if platform APIs fail.
                try { Activate(); } catch { }
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

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

        private void OnBarkAvatarClicked()
        {
            // Minimal Avalonia-side bark reaction: speak a random phrase when the avatar is clicked,
            // mirroring the WPF bark system's AvatarClicked trigger. Guarded so rapid clicks don't queue
            // an endless speech backlog.
            if (_isMuted || !IsAvatarVisibleOnScreen) return;
            if (!IsSpeechReady()) return;

            var phrase = GetRandomBambiPhrase();
            if (!string.IsNullOrWhiteSpace(phrase))
                Giggle(phrase);
        }

        private void OnBarkRequested(string kind)
        {
            // Chaos (and future) bark notifications: have the avatar react with a random phrase.
            // Rank-up and first-gold moments are treated as priority giggles so they aren't drowned
            // out by other chatter.
            if (_isMuted || !IsAvatarVisibleOnScreen) return;
            if (!IsSpeechReady()) return;

            var phrase = GetRandomBambiPhrase();
            if (string.IsNullOrWhiteSpace(phrase)) return;

            var priority = kind is "chaos.rankup" or "chaos.goldfirst" or "chaos.results";
            if (priority)
                GigglePriority(phrase, aiGenerated: false);
            else
                Giggle(phrase);
        }
    }
}
