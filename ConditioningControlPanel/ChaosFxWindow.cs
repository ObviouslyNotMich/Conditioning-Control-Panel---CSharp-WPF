using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel;

/// <summary>
/// Lightweight full-screen, click-through colour-vignette used for Chaos "impact"
/// juice. One instance per run; <see cref="Pulse"/> flashes a coloured edge-vignette
/// (red = detonation/malus, green = defuse, gold = combo milestone, blue = shield
/// save) that fades out fast. No screen capture or WebView — just a frozen
/// RadialGradientBrush + an opacity key-frame animation, so it's cheap to fire
/// rapidly when things get hectic, and it lands instantly (masking the heavier
/// overlay effects' split-second load).
/// </summary>
public sealed class ChaosFxWindow : Window
{
    private readonly Border _vignette;

    public ChaosFxWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        _vignette = new Border { Opacity = 0, IsHitTestVisible = false };
        Content = _vignette;
        SourceInitialized += (_, _) => ApplyExStyles();
    }

    /// <summary>Flash a coloured edge-vignette. <paramref name="strength"/> 0..1 scales peak opacity.</summary>
    public void Pulse(Color color, double strength)
    {
        try
        {
            double peak = Math.Clamp(0.22 + strength * 0.5, 0.15, 0.72);

            var brush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.9,
                RadiusY = 1.0
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, color.R, color.G, color.B), 1.0));
            brush.Freeze();
            _vignette.Background = brush;

            // Snap up (40ms), fade out (260ms) — reads as an impact.
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            _vignette.BeginAnimation(OpacityProperty, anim);
        }
        catch { }
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
