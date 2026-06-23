using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosCursorGlowOverlay: a soft pink-gold halo that follows the cursor.
/// Click-through + NOACTIVATE. Created once at run start, shown/hidden in place.
/// </summary>
public partial class ChaosCursorGlowOverlay : Window
{
    private readonly ILogger<ChaosCursorGlowOverlay> _logger;


    private const double SIZE = 76;

    private static ChaosCursorGlowOverlay? _active;

    private readonly Ellipse _halo;
    private readonly ScaleTransform _scale;
    private ScalePulse? _pulse;

    public ChaosCursorGlowOverlay()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosCursorGlowOverlay>>();
WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = SIZE;
        Height = SIZE;
        Position = new PixelPoint((int)(-SIZE * 2), (int)(-SIZE * 2));

        var brush = new RadialGradientBrush { GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xD7, 0x00), 0.18));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0xFF, 0x8F, 0xC8), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x4D, 0xC4), 1.0));

        _scale = new ScaleTransform(1, 1);
        _halo = new Ellipse
        {
            Width = SIZE, Height = SIZE,
            Fill = brush,
            IsHitTestVisible = false,
            IsVisible = false,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = _scale,
        };
        Content = _halo;

        Opened += (_, _) => ApplyExStyles();
    }

    public static void EnsureCreated()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                TryCreate();
            }
            else
            {
                Dispatcher.UIThread.Post(TryCreate);
            }
        }
        catch { }
    }

    private static void TryCreate()
    {
        try
        {
            if (_active == null) { _active = new ChaosCursorGlowOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
        }
        catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosCursorGlowOverlay>>().LogInformation("ChaosCursorGlow.EnsureCreated: {E}", ex.Message); }
    }

    public static void Arm()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosCursorGlowOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                    else if (!_active.IsVisible) ((global::Avalonia.Controls.Window)_active).Show();
                    _haloFor(_active).IsVisible = true;
                    AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    _active._pulse?.Dispose();
                    _active._pulse = new ScalePulse(_active._scale, 0.85, 1.12, 620);
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosCursorGlowOverlay>>().LogInformation("ChaosCursorGlow.Arm: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void Disarm()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active != null)
                    {
                        _haloFor(_active).IsVisible = false;
                        _active._pulse?.Dispose();
                        _active._pulse = null;
                        _active._scale.ScaleX = 1;
                        _active._scale.ScaleY = 1;
                    }
                }
                catch { }
            });
        }
        catch { }
    }

    public static void MoveToPx(double pxX, double pxY)
    {
        try
        {
            var w = _active;
            if (w == null || !_haloFor(w).IsVisible) return;
            var x = pxX - SIZE / 2;
            var y = pxY - SIZE / 2;
            w.Position = new PixelPoint((int)x, (int)y);
        }
        catch { }
    }

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var w =
_active;
                    _active = null;
                    w?.Close();
                }
                catch { }
            });
        }
        catch { }
    }

    private static Ellipse _haloFor(ChaosCursorGlowOverlay w) => w._halo;

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);
}
