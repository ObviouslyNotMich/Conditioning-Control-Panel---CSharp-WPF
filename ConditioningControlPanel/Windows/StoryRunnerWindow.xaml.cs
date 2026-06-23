using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Story;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The in-app VN runner for the Rabbit Hole opening (the "half musical"): it plays the story beats
    /// authored by the Python <c>vn_editor.py</c> into <c>assets/story/opening.json</c> — rendering each
    /// beat's background, positioned characters and speech bubble (a full-stage renderer mirroring the
    /// editor's stage) — and when it reaches a <c>popping_session</c> beat it hands off to
    /// <see cref="Services.Chaos.ChaosModeService.StartStoryRun"/> for a song-synced Chaos descent, then
    /// resumes the story when the run reports back. Linear for now (no branching). Dev-gated behind
    /// <c>AppSettings.StoryPreviewEnabled</c>.
    /// </summary>
    public partial class StoryRunnerWindow : Window
    {
        private List<StoryBeat> _beats = new();
        private int _index;
        private bool _sessionRunning;

        private StoryRunnerWindow()
        {
            InitializeComponent();
            Root.Opacity = 0;   // first beat fades in
            // The story owns the companion for the whole opening: hide + mute it, suppress unscripted
            // videos, and let the per-run chaos avatar logic stand down (see ChaosModeService.StoryUiActive).
            Services.Chaos.ChaosModeService.StoryUiActive = true;
            try { App.AvatarWindow?.SetStoryUiActive(true); } catch { }
            Loaded += (_, _) => { _animating = true; ShowBeat(); };
            SizeChanged += (_, _) => { if (!_sessionRunning && !_animating) RenderCurrentVnBeat(); };
            Closed += (_, _) =>
            {
                Services.Chaos.ChaosModeService.StoryUiActive = false;
                try { App.AvatarWindow?.SetStoryUiActive(false); } catch { }
            };
        }

        /// <summary>Load the opening and open the runner. Owner keeps it above the main window.</summary>
        public static void Launch(Window? owner)
        {
            var script = StoryScript.Load(StoryAssets.OpeningJson);
            if (script == null || script.Beats.Count == 0)
            {
                MessageBox.Show(
                    "No story found.\n\nExpected: " + StoryAssets.OpeningJson,
                    "The Rabbit Hole", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var win = new StoryRunnerWindow { _beats = script.Beats };
            if (owner != null) win.Owner = owner;
            win.Show();
        }

        // ---- beat flow (fade transitions between beats and into/out of a session) ----

        private bool _animating;

        private void ShowBeat()
        {
            if (_index >= _beats.Count) { Close(); return; }
            var b = _beats[_index];
            if (b.IsPoppingSession) { LaunchSession(b); return; }   // stays black, then hides for the run
            RenderVnBeat(b);
            FadeTo(1, 220, () => _animating = false);
        }

        private void Advance()
        {
            if (_sessionRunning || _animating) return;
            _animating = true;
            FadeTo(0, 150, () => { _index++; ShowBeat(); });
        }

        private void GoBack()
        {
            if (_sessionRunning || _animating || _index <= 0) return;
            _animating = true;
            FadeTo(0, 150, () => { _index--; ShowBeat(); });
        }

        // Fade the whole stage to the target opacity, then invoke done. Clears any in-flight animation.
        private void FadeTo(double to, double ms, Action? done)
        {
            var anim = new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            if (done != null) anim.Completed += (_, _) => done();
            Root.BeginAnimation(OpacityProperty, anim);
        }

        private void SetOpacityImmediate(double v)
        {
            Root.BeginAnimation(OpacityProperty, null);   // release the animation hold
            Root.Opacity = v;
        }

        // ---- popping-session handoff ----

        private void LaunchSession(StoryBeat beat)
        {
            var session = beat.Session!;
            var bg = StoryAssets.ResolveBackground(beat.Background);
            var song = StoryAssets.ResolveStoryPath(session.Song);
            var env = string.IsNullOrEmpty(session.Envelope) ? null : StoryAssets.ResolveStoryPath(session.Envelope!);

            if (App.Chaos == null || !File.Exists(song))
            {
                App.Logger?.Warning("StoryRunner: cannot start session (chaos={C}, song exists={E}) for {Id}",
                    App.Chaos != null, File.Exists(song), beat.Id);
                Advance();   // skip a broken session rather than dead-ending the story
                return;
            }

            _sessionRunning = true;
            _animating = false;
            SetOpacityImmediate(0);   // we faded to black already; hold it under the run
            Hide();                   // let the Chaos overlays own the screen
            App.Chaos.StartStoryRun(session, bg, song, env, OnSessionDone);
        }

        // Fired (on the UI thread) when the popping session ends — full = ran the whole song.
        private void OnSessionDone(bool full)
        {
            _sessionRunning = false;
            SetOpacityImmediate(0);   // come back from black, then fade the next beat in
            try { Show(); Activate(); } catch { }
            _index++;
            _animating = true;
            ShowBeat();
        }

        // ---- full-stage VN rendering (mirrors vn_editor.py render()) ----

        private void RenderVnBeat(StoryBeat b)
        {
            Show();
            RenderCurrentVnBeatCore(b);
        }

        private void RenderCurrentVnBeat()
        {
            if (_index < _beats.Count) RenderCurrentVnBeatCore(_beats[_index]);
        }

        private void RenderCurrentVnBeatCore(StoryBeat b)
        {
            if (b.IsPoppingSession) return;   // sessions aren't drawn here
            double w = Root.ActualWidth, h = Root.ActualHeight;
            if (w <= 0 || h <= 0) return;     // not laid out yet — Loaded/SizeChanged will re-run

            BgImage.Source = LoadBitmap(StoryAssets.ResolveBackground(b.Background));
            StageCanvas.Children.Clear();

            // Characters, back-to-front.
            foreach (var c in b.Characters)
            {
                var src = LoadBitmap(StoryAssets.ResolveCharacter(c.Image));
                if (src == null) continue;
                double aspect = src.PixelHeight > 0 ? (double)src.PixelWidth / src.PixelHeight : 1.0;
                double dispH = Math.Max(1, c.H * h);
                double dispW = dispH * aspect;
                var img = new Image
                {
                    Source = src,
                    Width = dispW,
                    Height = dispH,
                    Stretch = Stretch.Fill,
                    SnapsToDevicePixels = true
                };
                if (c.Flip)
                    img.RenderTransform = new ScaleTransform(-1, 1, dispW / 2, dispH / 2);
                Canvas.SetLeft(img, c.Cx * w - dispW / 2);
                Canvas.SetTop(img, c.Cy * h - dispH / 2);
                StageCanvas.Children.Add(img);
            }

            // Speech bubble (skip when there's nothing to say — a pure visual beat).
            if (!string.IsNullOrWhiteSpace(b.Text) || !string.IsNullOrWhiteSpace(b.Speaker))
                StageCanvas.Children.Add(BuildBubble(b, w, h));
        }

        private static FrameworkElement BuildBubble(StoryBeat b, double w, double h)
        {
            var panel = new StackPanel { MaxWidth = Math.Max(240, w * 0.6) };
            bool caption = string.IsNullOrWhiteSpace(b.Speaker);

            if (!caption)
                panel.Children.Add(new TextBlock
                {
                    Text = b.Speaker.ToUpperInvariant(),
                    FontWeight = FontWeights.Bold,
                    FontSize = 22,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                    Margin = new Thickness(0, 0, 0, 6)
                });

            panel.Children.Add(new TextBlock
            {
                Text = b.Text,
                FontSize = 24,
                FontStyle = caption ? FontStyles.Italic : FontStyles.Normal,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = caption ? TextAlignment.Center : TextAlignment.Left
            });

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x14, 0x10, 0x24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x69, 0xB4)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(22, 16, 22, 16),
                Child = panel
            };

            // Position so the bubble's center lands on (cx, cy). Measure to get its size first.
            border.Measure(new Size(Math.Max(240, w * 0.6), h));
            var sz = border.DesiredSize;
            Canvas.SetLeft(border, Math.Clamp(b.Bubble.Cx * w - sz.Width / 2, 8, Math.Max(8, w - sz.Width - 8)));
            Canvas.SetTop(border, Math.Clamp(b.Bubble.Cy * h - sz.Height / 2, 8, Math.Max(8, h - sz.Height - 8)));
            return border;
        }

        private static BitmapImage? LoadBitmap(string? path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex) { App.Logger?.Debug("StoryRunner LoadBitmap {P}: {E}", path, ex.Message); return null; }
        }

        // ---- input ----

        private void OnClick(object sender, MouseButtonEventArgs e) => Advance();

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (!_sessionRunning) Close();
                    break;
                case Key.Space:
                case Key.Enter:
                case Key.Right:
                    Advance();
                    break;
                case Key.Left:
                    GoBack();
                    break;
            }
        }
    }
}
