using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Small, movable splash shown while the webcam / eye-tracking engine
    /// starts up. WebcamTrackingService.Start() opens the camera and constructs
    /// three ONNX inference sessions on a worker thread, which can block several
    /// seconds (longer on first run or slow USB cameras). This window is driven
    /// by WebcamTrackingService.OnStartupProgress so the user sees what's
    /// happening instead of an unresponsive button, and can drag it out of the
    /// way while they wait.
    /// </summary>
    public partial class WebcamLoadingSplash : Window
    {
        private bool _closing;

        public WebcamLoadingSplash()
        {
            InitializeComponent();
            StartPulse();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Borderless window — let the user drag it anywhere.
            if (e.ChangedButton == MouseButton.Left)
            {
                // DragMove can throw if the button was already released by the
                // time it runs; the splash is cosmetic, so swallow it.
                try { DragMove(); } catch { }
            }
        }

        /// <summary>
        /// Update the progress bar (0.0–1.0) and status text. Safe to call from
        /// any thread.
        /// </summary>
        public void SetProgress(double progress, string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetProgress(progress, status)));
                return;
            }
            if (_closing) return;

            if (!string.IsNullOrEmpty(status)) TxtStatus.Text = status;

            var animation = new DoubleAnimation
            {
                To = Math.Min(1.0, Math.Max(0.0, progress)),
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        }

        /// <summary>
        /// Show a failure message on the splash, then auto-close after a short
        /// beat so the user can read WHY eye-tracking didn't start (camera in
        /// use, OS-denied, engine error, or open timed out) instead of the bar
        /// silently vanishing or hanging forever (#300, #311). Safe to call from
        /// any thread; idempotent with CloseSplash.
        /// </summary>
        public void ShowErrorAndClose(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowErrorAndClose(message)));
                return;
            }
            if (_closing) return;
            StopPulse();
            if (!string.IsNullOrEmpty(message)) TxtStatus.Text = message;
            ProgressFill.Opacity = 1.0;

            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2800) };
            hold.Tick += (s, e) => { hold.Stop(); CloseSplash(); };
            hold.Start();
        }

        /// <summary>
        /// Fade the splash out and close it. Safe to call from any thread, and
        /// idempotent.
        /// </summary>
        public void CloseSplash()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(CloseSplash));
                return;
            }
            if (_closing) return;
            _closing = true;
            StopPulse();

            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            fadeOut.Completed += (s, e) => { try { Close(); } catch { } };
            BeginAnimation(OpacityProperty, fadeOut);
        }

        // Gentle breathing pulse on the fill so the long phases (camera warmup,
        // ONNX session construction) don't look frozen between the discrete
        // progress jumps.
        private void StartPulse()
        {
            var pulse = new DoubleAnimation
            {
                From = 1.0,
                To = 0.55,
                Duration = TimeSpan.FromMilliseconds(900),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            ProgressFill.BeginAnimation(OpacityProperty, pulse);
        }

        private void StopPulse()
        {
            ProgressFill.BeginAnimation(OpacityProperty, null);
            ProgressFill.Opacity = 1.0;
        }
    }
}
