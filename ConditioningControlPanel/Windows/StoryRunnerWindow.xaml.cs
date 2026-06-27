using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Story;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The in-app VN runner for the Rabbit Hole opening (the "half musical"): plays the story beats
    /// authored by the Python <c>vn_editor.py</c> into <c>assets/story/opening.json</c> — rendering each
    /// beat's background, positioned characters and speech bubble — with authored TRANSITIONS between
    /// beats (cut / fade / crossfade / slide), and handing off to a song-synced Chaos run on a
    /// <c>popping_session</c> beat. Two stage layers let a transition show the old and new beat at once.
    /// Dev-gated behind <c>AppSettings.StoryPreviewEnabled</c>.
    /// </summary>
    public partial class StoryRunnerWindow : Window
    {
        private List<StoryBeat> _beats = new();
        private int _index;
        private bool _sessionRunning;
        private bool _animating;

        // Front = the beat currently shown; back = where the next beat is rendered before it transitions in.
        private Grid _front = null!, _back = null!;
        private Image _frontBg = null!, _backBg = null!;
        private Canvas _frontStage = null!, _backStage = null!;

        private StoryRunnerWindow()
        {
            InitializeComponent();
            _front = LayerA; _frontBg = BgA; _frontStage = StageA;
            _back = LayerB; _backBg = BgB; _backStage = StageB;
            _front.Opacity = 0; _back.Opacity = 0;   // start black; the first beat transitions in

            // The story owns the companion for the whole opening: hidden + muted, unscripted videos
            // suppressed, per-run chaos avatar logic stands down (see ChaosModeService.StoryUiActive).
            Services.Chaos.ChaosModeService.StoryUiActive = true;
            try { App.AvatarWindow?.SetStoryUiActive(true); } catch { }

            Loaded += (_, _) => { _animating = true; ShowBeat(); };
            SizeChanged += (_, _) => { if (!_sessionRunning && !_animating && Current != null) RenderBeatInto(_frontBg, _frontStage, Current); };
            Closed += (_, _) =>
            {
                Services.Chaos.ChaosModeService.StoryUiActive = false;
                try { App.AvatarWindow?.SetStoryUiActive(false); } catch { }
            };
        }

        private StoryBeat? Current => (_index >= 0 && _index < _beats.Count) ? _beats[_index] : null;

        /// <summary>Load the opening and open the runner. Owner keeps it above the main window.</summary>
        public static void Launch(Window? owner)
        {
            var script = StoryScript.Load(StoryAssets.OpeningJson);
            if (script == null || script.Beats.Count == 0)
            {
                MessageBox.Show("No story found.\n\nExpected: " + StoryAssets.OpeningJson,
                    "The Rabbit Hole", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var win = new StoryRunnerWindow { _beats = script.Beats };
            if (owner != null) win.Owner = owner;
            win.Show();
        }

        // ---- beat flow ----

        private void ShowBeat()
        {
            if (_index >= _beats.Count) { Close(); return; }
            var b = _beats[_index];
            if (b.IsPoppingSession) { LaunchSession(b); return; }
            RenderBeatInto(_backBg, _backStage, b);                 // render the new beat off-screen
            RunTransition(b.Transition, () => { SwapLayers(); _animating = false; });
        }

        private void Advance()
        {
            if (_sessionRunning || _animating) return;
            if (_index + 1 >= _beats.Count) { Close(); return; }
            _animating = true; _index++; ShowBeat();
        }

        private void GoBack()
        {
            if (_sessionRunning || _animating || _index <= 0) return;
            _animating = true; _index--; ShowBeat();
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
                _animating = false; Advance(); return;     // skip a broken session rather than dead-ending
            }

            _sessionRunning = true;
            // Fade the stage to black, then hand the screen to the Chaos overlays.
            AnimateOpacity(_front, _front.Opacity, 0, 200, () =>
            {
                _front.Opacity = 0; _back.Opacity = 0;
                _animating = false;
                Hide();
                App.Chaos.StartStoryRun(session, bg, song, env, OnSessionDone);
            });
        }

        // Fired (on the UI thread) when the popping session ends — full = ran the whole song.
        private void OnSessionDone(bool full)
        {
            _sessionRunning = false;
            _front.Opacity = 0; _back.Opacity = 0;   // come back from black; next beat transitions in
            try { Show(); Activate(); } catch { }
            _index++;
            _animating = true;
            ShowBeat();
        }

        // ---- transitions (honour beat.Transition; match the Python editor's preview) ----

        private void RunTransition(BeatTransition? t, Action done)
        {
            string type = (t?.Type ?? "fade").ToLowerInvariant();
            double ms = (t != null && t.Ms > 0) ? t.Ms : 300;
            double w = Math.Max(1, ActualWidth);

            Panel.SetZIndex(_back, 1); Panel.SetZIndex(_front, 0);   // incoming on top
            SetTx(_front, 0); SetTx(_back, 0);

            switch (type)
            {
                case "cut":
                    _front.Opacity = 0; _back.Opacity = 1; done(); break;

                case "crossfade":
                    _back.Opacity = 0;
                    AnimateOpacity(_back, 0, 1, ms, () => { _front.Opacity = 0; done(); });
                    break;

                case "slide_left":   // new beat enters from the right, pushing the old out left
                    _back.Opacity = 1; SetTx(_back, w);
                    AnimateTx(_front, 0, -w, ms, null);
                    AnimateTx(_back, w, 0, ms, () => { _front.Opacity = 0; SetTx(_front, 0); done(); });
                    break;

                case "slide_right":  // new beat enters from the left
                    _back.Opacity = 1; SetTx(_back, -w);
                    AnimateTx(_front, 0, w, ms, null);
                    AnimateTx(_back, -w, 0, ms, () => { _front.Opacity = 0; SetTx(_front, 0); done(); });
                    break;

                default:             // "fade" — through black: old out, then new in
                    AnimateOpacity(_front, _front.Opacity, 0, ms * 0.45, () =>
                    {
                        _back.Opacity = 0;
                        AnimateOpacity(_back, 0, 1, ms * 0.55, () => done());
                    });
                    break;
            }
        }

        private void SwapLayers()
        {
            (_front, _back) = (_back, _front);
            (_frontBg, _backBg) = (_backBg, _frontBg);
            (_frontStage, _backStage) = (_backStage, _frontStage);
        }

        private static TranslateTransform Tt(Grid g) => (TranslateTransform)g.RenderTransform;

        private static void SetTx(Grid g, double x)
        {
            var tt = Tt(g);
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.X = x;
        }

        private static void AnimateTx(Grid g, double from, double to, double ms, Action? done)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(Math.Max(1, ms)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            if (done != null) anim.Completed += (_, _) => done();
            Tt(g).BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private static void AnimateOpacity(UIElement el, double from, double to, double ms, Action? done)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(Math.Max(1, ms)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            if (done != null) anim.Completed += (_, _) => done();
            el.BeginAnimation(OpacityProperty, anim);
        }

        // ---- full-stage rendering (mirrors vn_editor.py render()) ----

        private void RenderBeatInto(Image bgImage, Canvas stage, StoryBeat b)
        {
            if (b.IsPoppingSession) return;
            double w = Root.ActualWidth, h = Root.ActualHeight;
            if (w <= 0 || h <= 0) return;

            bgImage.Source = LoadBitmap(StoryAssets.ResolveBackground(b.Background));
            stage.Children.Clear();

            foreach (var c in b.Characters)
            {
                var src = LoadBitmap(StoryAssets.ResolveCharacter(c.Image));
                if (src == null) continue;
                double aspect = src.PixelHeight > 0 ? (double)src.PixelWidth / src.PixelHeight : 1.0;
                double dispH = Math.Max(1, c.H * h);
                double dispW = dispH * aspect;
                var img = new Image { Source = src, Width = dispW, Height = dispH, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
                if (c.Flip) img.RenderTransform = new ScaleTransform(-1, 1, dispW / 2, dispH / 2);
                Canvas.SetLeft(img, c.Cx * w - dispW / 2);
                Canvas.SetTop(img, c.Cy * h - dispH / 2);
                stage.Children.Add(img);
            }

            if (!string.IsNullOrWhiteSpace(b.Text) || !string.IsNullOrWhiteSpace(b.Speaker))
                stage.Children.Add(BuildBubble(b, w, h));
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
                case Key.Escape: if (!_sessionRunning) Close(); break;
                case Key.Space:
                case Key.Enter:
                case Key.Right: Advance(); break;
                case Key.Left: GoBack(); break;
            }
        }
    }
}
