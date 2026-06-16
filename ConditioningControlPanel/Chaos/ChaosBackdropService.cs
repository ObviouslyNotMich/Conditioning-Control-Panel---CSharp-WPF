using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Per-zone backdrop plates rendered UNDER the chaos bubbles. The Step-1 layering spike proved
/// that bubbles/FX/HUD are each their own <b>Topmost</b> window, so a <b>non-topmost</b> fullscreen
/// window is deterministically below all of them — no z-order bookkeeping needed. Unlike the rest of
/// the chaos overlays this one is NOT click-through: it is the play surface and absorbs stray clicks
/// (bubbles still pop — they are topmost windows above it; the right-click Ripple is a global hook).
///
/// Gated entirely on <c>NarrativeModeEnabled &amp;&amp; BackdropEnabled</c>: when off, no window spawns
/// and classic Chaos keeps its desktop click-through behavior exactly. Art via <see cref="ChaosArt"/>
/// at <c>assets/Chaos/backdrops/depth{N}.png</c>; swaps on the depth/zone border.
/// </summary>
internal static class ChaosBackdropService
{
    private static Window? _active;
    private static Image? _img;
    private static int _currentDepth = -1;

    private static bool Enabled =>
        App.Settings?.Current?.NarrativeModeEnabled == true &&
        App.Settings?.Current?.BackdropEnabled == true;

    /// <summary>The live zone plate, for reuse as a story-card background. Null when no backdrop is up.</summary>
    public static ImageSource? CurrentSource => _img?.Source;

    /// <summary>Spawn the backdrop for a depth (act index, 1-based). No-op when the feature is off.</summary>
    public static void Show(int depth)
    {
        if (!Enabled) { CloseActive(); return; }
        try
        {
            if (_active == null) Build();
            SetDepth(depth);
        }
        catch (Exception ex) { App.Logger?.Warning("ChaosBackdropService.Show failed: {E}", ex.Message); }
    }

    /// <summary>Swap the plate when the run crosses a depth/zone border.</summary>
    public static void SwapTo(int depth)
    {
        if (!Enabled) return;
        if (_active == null) { Show(depth); return; }
        if (depth == _currentDepth) return;
        try { SetDepth(depth); } catch (Exception ex) { App.Logger?.Debug("ChaosBackdropService.SwapTo: {E}", ex.Message); }
    }

    public static void CloseActive()
    {
        try { _active?.Close(); } catch { }
        _active = null; _img = null; _currentDepth = -1;
    }

    private static void SetDepth(int depth)
    {
        if (_img == null) return;
        _currentDepth = depth;
        var src = ChaosArt.Resolve("backdrops", "depth" + depth);
        _img.Source = src;
        if (_active != null) _active.Opacity = App.Settings?.Current?.BackdropOpacity ?? 0.55;
        // brief fade so a border-swap reads as a change of scene rather than a hard cut
        _img.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(350)));
    }

    private static void Build()
    {
        _img = new Image { Stretch = Stretch.UniformToFill };
        var grid = new Grid();
        grid.Children.Add(_img);

        _active = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Black,    // covers the desktop under the (semi-opaque) window
            Topmost = false,               // proven: sits under every topmost bubble/overlay window
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = SystemParameters.PrimaryScreenWidth,
            Height = SystemParameters.PrimaryScreenHeight,
            Opacity = App.Settings?.Current?.BackdropOpacity ?? 0.55,
            Content = grid,
        };
        _active.SourceInitialized += (_, _) => ApplyExStyles(_active);
        _active.Show();
        App.Logger?.Information("ChaosBackdropService window up (non-topmost, click-absorbing)");
    }

    // Absorb clicks (NO WS_EX_TRANSPARENT) but never steal focus / show in Alt-Tab.
    private static void ApplyExStyles(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
