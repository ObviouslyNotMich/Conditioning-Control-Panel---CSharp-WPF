using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// EXPERIMENTAL shared-host for chaos bubbles (gated by AppSettings.ChaosBubbleSharedHost).
///
/// Instead of one top-level layered <see cref="Window"/> per bubble — each repositioned via
/// platform window moves every frame, which saturates the UI thread and starves click input under
/// a dense field — every bubble's visual lives as a child of this ONE full-virtual-screen
/// Canvas, positioned with <see cref="Canvas"/>.SetLeft/Top (cheap, batched in one render pass).
///
/// The window is fully CLICK-THROUGH: empty space passes clicks to the desktop, and pops are
/// detected by the global input hook (future BubbleService port) which swallows a hit. No Avalonia
/// hit-testing happens here.
///
/// Keep-alive contract like every chaos overlay: created once at run start, closed only at teardown
/// — layered-window churn deadlocks the render thread. All Add/Remove/Place calls run on the UI
/// thread (spawn/animate/destroy already do).
/// </summary>
public sealed class ChaosBubbleHostOverlay : Window
{
    private static ChaosBubbleHostOverlay? _active;
    private readonly Canvas _canvas;
    private readonly IAppLogger? _logger;

    public static bool IsReady => _active != null;

    private ChaosBubbleHostOverlay()
    {
        _logger = App.Services?.GetService<IAppLogger>();

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

        var (sl, st, sw, sh) = AvaloniaChaosWindowZ.StageBounds();
        Position = new PixelPoint((int)sl, (int)st);
        Width = sw;
        Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        Opened += (_, _) => ApplyExStyles();
    }

    /// <summary>Create + show the host at run start (shared-host mode only).</summary>
    public static void EnsureCreated()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                TryCreate();
            else
                Dispatcher.UIThread.Post(TryCreate);
        }
        catch { }
    }

    private static void TryCreate()
    {
        try
        {
            if (_active != null) return;
            _active = new ChaosBubbleHostOverlay();
            _active.Show();
            AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
        }
        catch (Exception ex)
        {
            // swallow; diagnostics must never break a run
        }
    }

    /// <summary>Add a bubble visual to the host (UI thread). No-op if the host isn't up.</summary>
    public static void Add(Control el)
    {
        try
        {
            if (_active != null && el != null && !_active._canvas.Children.Contains(el))
                _active._canvas.Children.Add(el);
        }
        catch { }
    }

    /// <summary>Remove a bubble visual from the host (UI thread).</summary>
    public static void Remove(Control el)
    {
        try { if (_active != null && el != null) _active._canvas.Children.Remove(el); }
        catch { }
    }

    /// <summary>Position a bubble visual. Coordinates are GLOBAL DIPs; the host subtracts its own
    /// origin so the child lands in canvas-local space. UI thread only.</summary>
    public static void Place(Control el, double globalLeftDip, double globalTopDip)
    {
        var w = _active;
        if (w == null || el == null) return;
        Canvas.SetLeft(el, globalLeftDip - w.Position.X);
        Canvas.SetTop(el, globalTopDip - w.Position.Y);
    }

    /// <summary>Re-stack the live host above a mandatory video. UI thread only.</summary>
    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Instant teardown (run end / shutdown) — the only place this window dies.</summary>
    public static void CloseActive()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                TryClose();
            else
                Dispatcher.UIThread.Post(TryClose);
        }
        catch { }
    }

    private static void TryClose()
    {
        try
        {
            var w = _active;
            _active = null;
            if (w != null)
            {
                w._canvas.Children.Clear();
                w.Close();
            }
        }
        catch { }
    }

    private void ApplyExStyles()
    {
        // TODO: apply WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT on Windows.
    }
}
