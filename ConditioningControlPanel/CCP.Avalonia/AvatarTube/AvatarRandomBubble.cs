using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    /// <summary>
    /// A clickable bubble that spawns near the avatar and floats upward.
    /// </summary>
    internal class AvatarRandomBubble
    {
        private readonly Window _window;
        private readonly DispatcherTimer _animTimer;
        private readonly Random _random;
        private readonly Action _onPop;
        private readonly Image _bubbleImage;

        private double _posX, _posY;
        private double _startX;
        private double _speed;
        private double _timeAlive;
        private double _wobbleOffset;
        private double _angle;
        private double _scale = 1.0;
        private double _fadeAlpha = 1.0;
        private int _animType;
        private bool _isPopping;
        private bool _isAlive = true;

        private readonly int _size;
        private readonly double _screenTop;

        public AvatarRandomBubble(global::Avalonia.Point avatarScreenPos, Random random, Action onPop)
            : this(avatarScreenPos, random, onPop, App.Services.GetService<IAssetLoader>())
        {
        }

        public AvatarRandomBubble(global::Avalonia.Point avatarScreenPos, Random random, Action onPop, IAssetLoader? assetLoader)
        {
            _random = random;
            _onPop = onPop;

            _size = random.Next(80, 130);
            _speed = 1.0 + random.NextDouble() * 1.0;
            _animType = random.Next(4);
            _wobbleOffset = random.NextDouble() * 100;
            _angle = random.Next(360);

            double dpiScale = 1.0;
            _startX = avatarScreenPos.X / dpiScale + 50 + random.Next(-30, 30);
            _posX = _startX;
            _posY = avatarScreenPos.Y / dpiScale;
            _screenTop = -_size - 50;

            _bubbleImage = new Image
            {
                Width = _size,
                Height = _size,
                Stretch = Stretch.Uniform,
                Source = LoadBubbleImage(assetLoader),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                Cursor = new Cursor(StandardCursorType.Hand),
                IsHitTestVisible = true
            };

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(1, 1));
            transformGroup.Children.Add(new RotateTransform(0));
            _bubbleImage.RenderTransform = transformGroup;

            _bubbleImage.PointerPressed += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };

            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                IsHitTestVisible = true
            };
            grid.Children.Add(_bubbleImage);
            grid.PointerPressed += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };

            _window = new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Width = _size + 40,
                Height = _size + 40,
                Position = new PixelPoint((int)(_posX + 2), (int)(_posY - 20)),
                Content = grid,
                Cursor = new Cursor(StandardCursorType.Hand),
                IsHitTestVisible = true
            };

            _window.PointerPressed += (s, e) => Pop();
            _window.Show();

            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _animTimer.Tick += Animate;
            _animTimer.Start();
        }

        private static Bitmap? LoadBubbleImage(IAssetLoader? loader)
        {
            try
            {
                return AvaloniaBitmapHelper.LoadResource("bubble.png");
            }
            catch
            {
                return null;
            }
        }

        private void Animate(object? sender, EventArgs e)
        {
            if (!_isAlive) return;

            if (_isPopping)
            {
                _scale += 0.06;
                _fadeAlpha -= 0.1;
                _angle += 3;
                if (_fadeAlpha <= 0) { Destroy(); return; }
            }
            else
            {
                _timeAlive += 0.03;
                _posY -= _speed;
                double offset = _animType switch
                {
                    0 => Math.Sin(_timeAlive * 2) * 25,
                    1 => Math.Sin(_timeAlive * 2.5) * 30,
                    2 => Math.Cos(_timeAlive * 1.8) * 25,
                    _ => Math.Sin(_timeAlive) * 30 + Math.Cos(_timeAlive * 2) * 15
                };
                _angle = (_angle + (_animType == 2 ? -1.0 : 0.5)) % 360;
                _posX = _startX + offset;
                if (_posY < _screenTop) { Destroy(); return; }
            }

            try
            {
                var wobble = 0.06 * Math.Sin(_timeAlive * 2.5 + _wobbleOffset);
                var currentScale = _scale + wobble;
                if (_bubbleImage.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
                {
                    if (tg.Children[0] is ScaleTransform st) { st.ScaleX = currentScale; st.ScaleY = currentScale; }
                    if (tg.Children[1] is RotateTransform rt) rt.Angle = _angle;
                }
                _window.Opacity = _fadeAlpha;
                _window.Position = new PixelPoint((int)(_posX + 2), (int)(_posY - 20));
            }
            catch
            {
                Destroy();
            }
        }

        public void Pop()
        {
            if (!_isAlive || _isPopping) return;
            _isPopping = true;
            _onPop?.Invoke();
        }

        private void Destroy()
        {
            if (!_isAlive) return;
            _isAlive = false;
            _animTimer.Stop();
            try { _window.Close(); } catch { }
        }
    }
}
