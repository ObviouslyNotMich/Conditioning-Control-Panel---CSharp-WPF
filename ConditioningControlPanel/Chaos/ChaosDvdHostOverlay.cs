using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ConditioningControlPanel;

/// <summary>
/// EXPERIMENTAL shared-host for the DVD bouncing-text logos (gated by AppSettings.ChaosDvdSharedHost).
///
/// Instead of one top-level layered <see cref="Window"/> per logo — each repositioned via SetWindowPos
/// every frame, which saturates the UI thread and freezes the companion avatar when a split spawns up
/// to ~16 logos — every logo's visual (its <c>_host</c> Grid) lives as a child of this ONE full-stage
/// Canvas, positioned with <see cref="Canvas"/>.SetLeft/Top (cheap, batched in one render pass).
///
/// Fully CLICK-THROUGH (WS_EX_TRANSPARENT): the only clickable logos are the Spanker capstone, which
/// keep the per-window path so their smack still hit-tests — nothing here needs WPF input.
///
/// Keep-alive contract like every chaos overlay (see <see cref="ChaosBubbleHostOverlay"/>): created
/// once at run start, closed only at teardown — layered-window churn deadlocks the render thread.
/// All Add/Remove/Place calls run on the UI thread (ChaosDvdOverlay's launch/Step/retire already do).
/// </summary>
public sealed class ChaosDvdHostOverlay : Window
{
    private static ChaosDvdHostOverlay? _active;
    private readonly Canvas _canvas;

    public static bool IsReady => _active != null;

    /// <summary>Create + show the host at run start (shared-host mode only).</summary>
    public static void EnsureCreated()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (disp.CheckAccess()) { CreateNow(); return; }
            disp.BeginInvoke(new Action(CreateNow));
        }
        catch { }
    }

    private static void CreateNow()
    {
        try
        {
            if (_active == null)
            {
                _active = new ChaosDvdHostOverlay();
                ((Window)_active).Show();
                ChaosWindowZ.RaiseAboveVideo(_active);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosDvdHost.EnsureCreated: {E}", ex.Message); }
    }

    /// <summary>Add a logo visual to the host (UI thread). No-op if the host isn't up.</summary>
    public static void Add(UIElement el)
    {
        try { if (_active != null && el != null && !_active._canvas.Children.Contains(el)) _active._canvas.Children.Add(el); }
        catch { }
    }

    /// <summary>Remove a logo visual from the host (UI thread).</summary>
    public static void Remove(UIElement el)
    {
        try { if (_active != null && el != null) _active._canvas.Children.Remove(el); }
        catch { }
    }

    /// <summary>Position a logo visual. Coordinates are GLOBAL DIPs (the same space the per-logo
    /// Window.Left/Top used); the host subtracts its own origin so the child lands in canvas-local
    /// space. UI thread only.</summary>
    public static void Place(UIElement el, double globalLeftDip, double globalTopDip)
    {
        var w = _active;
        if (w == null || el == null) return;
        Canvas.SetLeft(el, globalLeftDip - w.Left);
        Canvas.SetTop(el, globalTopDip - w.Top);
    }

    /// <summary>Re-stack the live host above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Instant teardown (run end / shutdown) — the only place this hwnd dies.</summary>
    public static void CloseActive()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) { _active = null; return; }
            disp.BeginInvoke(new Action(() =>
            {
                try
                {
                    var w = _active;
                    _active = null;
                    if (w != null) { w._canvas.Children.Clear(); w.Close(); }
                }
                catch { }
            }));
        }
        catch { }
    }

    private ChaosDvdHostOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;   // click-through; clickable (Spanker) logos use the per-window path
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var (sl, st, sw, sh) = ChaosWindowZ.StageBounds();
        Left = sl; Top = st; Width = sw; Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        SourceInitialized += (_, _) => ApplyExStyles();
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
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
