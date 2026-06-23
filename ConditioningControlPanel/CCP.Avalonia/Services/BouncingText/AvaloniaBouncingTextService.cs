using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.BouncingText;

/// <summary>
/// Avalonia implementation of the bouncing-text effect engine.
/// Renders a DVD-screensaver-style phrase that drifts across the desktop,
/// bounces off edges, awards XP, and tracks corner-hit achievements.
/// </summary>
public sealed class AvaloniaBouncingTextService : IBouncingTextService, IDisposable
{
    private const int BASE_FONT_SIZE = 72;
    private const double CORNER_TOLERANCE = 15.0;
    private static readonly TimeSpan BounceXpCooldown = TimeSpan.FromSeconds(2);
    private const int MaxBounceXpPerMinute = 150;

    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IAchievementService _achievements;
    private readonly IProgressionService _progression;
    private readonly ILogger<AvaloniaBouncingTextService>? _logger;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly List<BouncingTextWindow> _windows = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private string _currentText = "";
    private double _posX, _posY;
    private double _velX, _velY;
    private double _minX, _minY, _maxX, _maxY;
    private double _textWidth = 200;
    private double _textHeight = 60;
    private int _currentFontSize = BASE_FONT_SIZE;
    private int _currentOpacity = 100;
    private Color _currentColor = Colors.HotPink;
    private DateTime _lastBounceXpTime = DateTime.MinValue;
    private int _bounceXpThisMinute;
    private DateTime _bounceXpMinuteStart = DateTime.MinValue;

    public AvaloniaBouncingTextService(
        ISettingsService settings,
        IScreenProvider screens,
        IAchievementService achievements,
        IProgressionService progression,
        ILogger<AvaloniaBouncingTextService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _achievements = achievements ?? throw new ArgumentNullException(nameof(achievements));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _logger = logger;
        _timer.Tick += Animate;
    }

    public bool IsRunning { get; private set; }

    public event EventHandler? OnBounce;
    public event EventHandler? OnCornerHit;

    public void Start(IEnumerable<string>? textPool = null)
    {
        if (IsRunning) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaBouncingTextService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        var settings = _settings.Current;
        if (settings == null) return;

        IsRunning = true;
        _currentFontSize = (int)(BASE_FONT_SIZE * settings.BouncingTextSize / 100.0);
        _currentOpacity = settings.BouncingTextOpacity;

        SelectRandomText(textPool?.ToList());
        MeasureTextSize();
        CalculateScreenBounds(settings.DualMonitorEnabled);

        _posX = _minX + _random.NextDouble() * Math.Max(1, (_maxX - _minX - _textWidth));
        _posY = _minY + _random.NextDouble() * Math.Max(1, (_maxY - _minY - _textHeight));

        var speed = settings.BouncingTextSpeed / 10.0;
        var baseSpeed = (3.0 + _random.NextDouble() * 2.0) * 60.0;
        _velX = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);
        _velY = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);

        _currentColor = GetRandomColor();

        CreateWindows(settings.DualMonitorEnabled);
        _timer.Start();

        _logger?.LogInformation("AvaloniaBouncingTextService started - Text: {Text}, Size: {W}x{H}", _currentText, _textWidth, _textHeight);
    }

    public void Stop()
    {
        if (!IsRunning && _windows.Count == 0) return;
        IsRunning = false;
        _timer.Stop();

        lock (_sync)
        {
            foreach (var window in _windows)
            {
                try { window.Close(); } catch { }
            }
            _windows.Clear();
        }

        _logger?.LogInformation("AvaloniaBouncingTextService stopped");
    }

    public void Refresh()
    {
        if (!IsRunning) return;
        var settings = _settings.Current;
        if (settings == null) return;

        var speed = settings.BouncingTextSpeed / 10.0;
        var currentSpeed = Math.Sqrt(_velX * _velX + _velY * _velY);
        var targetSpeed = (3.0 + _random.NextDouble() * 2.0) * 60.0 * speed;
        var scale = targetSpeed / Math.Max(0.1, currentSpeed);
        _velX *= scale;
        _velY *= scale;

        var newFontSize = (int)(BASE_FONT_SIZE * settings.BouncingTextSize / 100.0);
        if (newFontSize != _currentFontSize)
        {
            _currentFontSize = newFontSize;
            MeasureTextSize();
            lock (_sync)
            {
                foreach (var window in _windows)
                    window.UpdateFontSize(_currentFontSize);
            }
        }

        var newOpacity = settings.BouncingTextOpacity;
        if (newOpacity != _currentOpacity)
        {
            _currentOpacity = newOpacity;
            lock (_sync)
            {
                foreach (var window in _windows)
                    window.UpdateOpacity(_currentOpacity);
            }
        }
    }

    private void SelectRandomText(List<string>? pool = null)
    {
        var settings = _settings.Current;
        if (settings == null)
        {
            _currentText = "GOOD GIRL";
            return;
        }

        var enabledTexts = pool != null && pool.Count > 0
            ? pool
            : settings.BouncingTextPool
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

        _currentText = enabledTexts.Count > 0
            ? enabledTexts[_random.Next(enabledTexts.Count)]
            : "GOOD GIRL";
    }

    private void MeasureTextSize()
    {
        try
        {
            var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
            var ft = new FormattedText(
                _currentText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                _currentFontSize,
                Brushes.White);

            _textWidth = ft.WidthIncludingTrailingWhitespace;
            _textHeight = ft.Height;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AvaloniaBouncingTextService: failed to measure text, using estimate");
            _textWidth = _currentFontSize * _currentText.Length * 0.6;
            _textHeight = _currentFontSize * 1.2;
        }
    }

    private void CalculateScreenBounds(bool dualMonitor)
    {
        var screens = GetScreens(dualMonitor);
        _minX = screens.Min(s => s.Bounds.X / s.Scaling);
        _minY = screens.Min(s => s.Bounds.Y / s.Scaling);
        _maxX = screens.Max(s => (s.Bounds.X + s.Bounds.Width) / s.Scaling);
        _maxY = screens.Max(s => (s.Bounds.Y + s.Bounds.Height) / s.Scaling);
    }

    private void CreateWindows(bool dualMonitor)
    {
        var screens = GetScreens(dualMonitor);
        lock (_sync)
        {
            foreach (var screen in screens)
            {
                var window = new BouncingTextWindow(screen, _currentFontSize, _currentOpacity);
                window.Show();
                OverlayZ.Register(window, OverlayZ.Layer.BouncingText);
                _windows.Add(window);
            }
            UpdateWindowsText();
            UpdateWindowsPosition();
        }
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (!IsRunning) return;
        const double dt = 1.0 / 60.0;

        _posX += _velX * dt;
        _posY += _velY * dt;

        var textRight = _posX + _textWidth;
        var textBottom = _posY + _textHeight;
        var bouncedX = false;
        var bouncedY = false;

        if (_posX <= _minX)
        {
            _posX = _minX;
            _velX = Math.Abs(_velX);
            bouncedX = true;
        }
        else if (textRight >= _maxX)
        {
            _posX = _maxX - _textWidth;
            _velX = -Math.Abs(_velX);
            bouncedX = true;
        }

        if (_posY <= _minY)
        {
            _posY = _minY;
            _velY = Math.Abs(_velY);
            bouncedY = true;
        }
        else if (textBottom >= _maxY)
        {
            _posY = _maxY - _textHeight;
            _velY = -Math.Abs(_velY);
            bouncedY = true;
        }

        var bounced = bouncedX || bouncedY;
        if (bouncedX && bouncedY)
        {
            _logger?.LogInformation("AvaloniaBouncingTextService: corner hit at ({X}, {Y})", _posX, _posY);
            _achievements.TrackCornerHit();
            OnCornerHit?.Invoke(this, EventArgs.Empty);
        }
        else if (bounced && IsNearCorner(_posX, _posY, textRight, textBottom))
        {
            _logger?.LogInformation("AvaloniaBouncingTextService: near-corner hit at ({X}, {Y})", _posX, _posY);
            _achievements.TrackCornerHit();
            OnCornerHit?.Invoke(this, EventArgs.Empty);
        }

        if (bounced)
        {
            _currentColor = GetRandomColor();
            var now = DateTime.UtcNow;
            if ((now - _bounceXpMinuteStart).TotalSeconds >= 60)
            {
                _bounceXpThisMinute = 0;
                _bounceXpMinuteStart = now;
            }
            if (now - _lastBounceXpTime >= BounceXpCooldown && _bounceXpThisMinute < MaxBounceXpPerMinute)
            {
                _progression.AddXP(15, XPSource.BouncingText);
                _lastBounceXpTime = now;
                _bounceXpThisMinute += 15;
            }
            OnBounce?.Invoke(this, EventArgs.Empty);

            if (_random.NextDouble() < 0.1)
            {
                SelectRandomText();
                MeasureTextSize();
            }

            UpdateWindowsText();
        }

        // Relative z-order is owned by OverlayZ (the shared coordinator); no per-service re-pinning.
        UpdateWindowsPosition();
    }

    private bool IsNearCorner(double left, double top, double right, double bottom)
    {
        return (left <= _minX + CORNER_TOLERANCE && top <= _minY + CORNER_TOLERANCE)
            || (right >= _maxX - CORNER_TOLERANCE && top <= _minY + CORNER_TOLERANCE)
            || (left <= _minX + CORNER_TOLERANCE && bottom >= _maxY - CORNER_TOLERANCE)
            || (right >= _maxX - CORNER_TOLERANCE && bottom >= _maxY - CORNER_TOLERANCE);
    }

    private void UpdateWindowsText()
    {
        lock (_sync)
        {
            foreach (var window in _windows)
                window.UpdateText(_currentText, _currentColor);
        }
    }

    private void UpdateWindowsPosition()
    {
        lock (_sync)
        {
            foreach (var window in _windows)
                window.UpdatePosition(_posX, _posY, _textWidth, _textHeight);
        }
    }

    private Color GetRandomColor()
    {
        var colors = new[]
        {
            Colors.HotPink, Colors.DeepPink, Colors.BlueViolet,
            Colors.Magenta, Colors.Cyan, Colors.Yellow,
            Colors.Lime, Colors.Orange, Colors.OrangeRed, Colors.LimeGreen
        };
        return colors[_random.Next(colors.Length)];
    }

    private IReadOnlyList<ScreenInfo> GetScreens(bool dualMonitor)
    {
        try
        {
            var all = _screens.GetAllScreens();
            if (all.Count == 0)
                return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };

            if (!dualMonitor)
            {
                var primary = _screens.GetPrimaryScreen() ?? all[0];
                return new[] { primary };
            }
            return all;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaBouncingTextService: could not enumerate screens: {Error}", ex.Message);
            return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed class BouncingTextWindow : Window
    {
        private readonly TextBlock _textBlock;
        private readonly ScreenInfo _screen;

        public BouncingTextWindow(ScreenInfo screen, int fontSize, int opacity)
        {
            _screen = screen;

            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;

            this.ConstrainToScreen(screen);

            _textBlock = new TextBlock
            {
                FontSize = fontSize,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.HotPink,
                Opacity = opacity / 100.0
            };

            var canvas = new Canvas();
            canvas.Children.Add(_textBlock);
            Content = canvas;

            Opened += (_, _) => ApplyPlatformStyles();
        }

        public void UpdateText(string text, Color color)
        {
            _textBlock.Text = text;
            _textBlock.Foreground = new SolidColorBrush(color);
        }

        public void UpdateFontSize(int fontSize)
        {
            _textBlock.FontSize = fontSize;
        }

        public void UpdateOpacity(int opacity)
        {
            _textBlock.Opacity = opacity / 100.0;
        }

        public void UpdatePosition(double globalX, double globalY, double textWidth, double textHeight)
        {
            var scale = _screen.Scaling > 0 ? _screen.Scaling : 1.0;
            var localX = globalX - _screen.Bounds.X / scale;
            var localY = globalY - _screen.Bounds.Y / scale;
            Canvas.SetLeft(_textBlock, localX);
            Canvas.SetTop(_textBlock, localY);
            _textBlock.IsVisible = true;
        }

        private void ApplyPlatformStyles()
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd == IntPtr.Zero) return;

                var exStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
                exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
            catch { }
        }

        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TRANSPARENT = 0x00000020;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);
        private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
            => IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLong64(hWnd, nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
            => IntPtr.Size == 4 ? SetWindowLong32(hWnd, nIndex, dwNewLong) : SetWindowLong64(hWnd, nIndex, dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    }
}
