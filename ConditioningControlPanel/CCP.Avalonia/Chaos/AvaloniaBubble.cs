using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia visual for a single ambient or chaos bubble.
/// Displays a bubble image with a simple tint overlay, a centered label, a fuse ring,
/// and supports a click + pop animation.
/// </summary>
public sealed class AvaloniaBubble : Panel
{
    private readonly Image _image;
    private readonly Border _tint;
    private readonly Border _fuseRing;
    private readonly Border _shieldRing;
    private readonly Border _crackRing;
    private readonly TextBlock _label;
    private readonly ScaleTransform _scaleTransform;
    private bool _popping;
    private bool _fading;
    private double _fuseFraction = 1.0;

    /// <summary>Bubble identifier used to route pointer events back to the engine.</summary>
    public Guid StateId { get; set; }

    public AvaloniaBubble(Bitmap? bitmap, double size)
    {
        Width = size;
        Height = size;
        Background = Brushes.Transparent;
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        _scaleTransform = new ScaleTransform(1, 1);
        RenderTransform = _scaleTransform;

        if (bitmap != null)
        {
            _image = new Image
            {
                Source = bitmap,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform
            };
            Children.Add(_image);
        }
        else
        {
            // Fallback circle when the bubble asset cannot be loaded.
            var fallbackPink = AppColor("PinkColor", new Color(0xFF, 0xFF, 0x69, 0xB4));
            var circle = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = new SolidColorBrush(fallbackPink)
            };
            Children.Add(circle);
        }

        var pink = AppColor("PinkColor", new Color(0xFF, 0xFF, 0x69, 0xB4));
        _tint = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(new Color(0x40, pink.R, pink.G, pink.B)),
            IsHitTestVisible = false
        };
        Children.Add(_tint);

        _fuseRing = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(Math.Max(2.0, size / 24.0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
        _fuseRing.Opacity = 0.0;
        Children.Add(_fuseRing);

        _shieldRing = new Border
        {
            Width = size * 1.25,
            Height = size * 1.25,
            CornerRadius = new CornerRadius(size * 1.25 / 2.0),
            BorderThickness = new Thickness(Math.Max(2.0, size / 20.0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0xCC, 0xFF)),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Opacity = 0.0
        };
        Children.Add(_shieldRing);

        _crackRing = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2.0),
            BorderThickness = new Thickness(Math.Max(2.0, size / 18.0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Opacity = 0.0
        };
        Children.Add(_crackRing);

        _label = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = Math.Max(8.0, size / 6.0),
            FontWeight = global::Avalonia.Media.FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            IsHitTestVisible = false,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = size * 0.85
        };
        Children.Add(_label);

        PointerPressed += OnPointerPressed;
        PointerReleased += OnBubblePointerReleased;
    }

    /// <summary>Raised when the bubble is clicked (left button press).</summary>
    public event EventHandler? Click;

    /// <summary>Raised when the left pointer is released over this bubble.</summary>
    public event EventHandler? BubblePointerReleased;

    /// <summary>Updates visual scale and opacity.</summary>
    public void SetVisual(double scale, double opacity)
    {
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
        Opacity = opacity;
    }

    /// <summary>Updates the centered label text.</summary>
    public void SetLabel(string label)
    {
        _label.Text = label ?? "";
    }

    /// <summary>Updates the tint overlay color while keeping the existing alpha.</summary>
    public void SetTint(byte r, byte g, byte b)
    {
        if (_tint.Background is SolidColorBrush brush)
        {
            _tint.Background = new SolidColorBrush(Color.FromArgb(brush.Color.A, r, g, b));
        }
    }

    /// <summary>Shows or hides the chaperone shield ring.</summary>
    public void SetShielded(bool shielded)
    {
        _shieldRing.Opacity = shielded ? 1.0 : 0.0;
    }

    /// <summary>Shows or hides the brittle crack overlay.</summary>
    public void SetBrittle(bool brittle)
    {
        _crackRing.Opacity = brittle ? 1.0 : 0.0;
    }

    /// <summary>Updates the fuse ring progress (0..1). Hidden when fraction is 1 or no fuse is set.</summary>
    public void SetFuse(double fraction)
    {
        _fuseFraction = Math.Clamp(fraction, 0.0, 1.0);
        _fuseRing.Opacity = _fuseFraction < 1.0 && _fuseFraction > 0.0 ? 1.0 : 0.0;

        // Shrink the ring slightly as the fuse burns down for a simple visual cue.
        var inset = (1.0 - _fuseFraction) * 0.15;
        _fuseRing.Margin = new Thickness(inset * Width);

        // Shift color from white -> yellow -> red as the fuse depletes.
        byte r, g;
        if (_fuseFraction > 0.5)
        {
            r = 0xFF;
            g = (byte)(0xFF * (1.0 - _fuseFraction) * 2.0);
        }
        else
        {
            r = (byte)(0xFF * _fuseFraction * 2.0);
            g = 0xFF;
        }
        _fuseRing.BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, r, g, 0x00));
    }

    /// <summary>
    /// Animates the bubble shrinking and fading out, then invokes <paramref name="onComplete"/>.
    /// </summary>
    public void Pop(Action? onComplete)
    {
        if (_popping || _fading) return;
        _popping = true;
        AnimateVisual(durationMs: 150.0, shrink: true, fade: true, onComplete);
    }

    /// <summary>
    /// Fades the bubble out without scaling, then invokes <paramref name="onComplete"/>.
    /// Used for short-lived prism ghost shadows.
    /// </summary>
    public void FadeOut(double durationMs, Action? onComplete)
    {
        if (_popping || _fading) return;
        _fading = true;
        AnimateVisual(durationMs, shrink: false, fade: true, onComplete);
    }

    private void AnimateVisual(double durationMs, bool shrink, bool fade, Action? onComplete)
    {
        var elapsed = 0.0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            elapsed += 16;
            var p = Math.Min(1.0, elapsed / durationMs);

            if (shrink)
            {
                var scale = 1.0 - p;
                _scaleTransform.ScaleX = scale;
                _scaleTransform.ScaleY = scale;
            }

            if (fade)
                Opacity = 1.0 - p;

            if (p >= 1.0)
            {
                timer.Stop();
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            Click?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBubblePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            e.Handled = true;
            BubblePointerReleased?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Color AppColor(string key, Color fallback)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is Color c)
            return c;
        return fallback;
    }
}
