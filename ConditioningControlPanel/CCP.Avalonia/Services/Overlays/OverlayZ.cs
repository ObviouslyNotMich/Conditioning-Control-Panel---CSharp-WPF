using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Services.Overlays;

/// <summary>
/// One-time z-band placement for effect windows. This is NOT a continuous coordinator.
///
/// History / why this shape: an earlier version re-stacked every registered window on a timer and
/// on every flash open. Re-pinning the layered tint/spiral windows to <c>HWND_TOPMOST</c>
/// recomposites them, and under heavy flash churn (a new full-screen window ~10×/sec) that made the
/// tint and spiral visibly blink — appearing to vanish for a frame and come back. The WPF original
/// never re-pins: overlays are born topmost once and left completely alone.
///
/// This version splits windows into two z-bands by rank and places each window ONCE:
///  - rank ≥ <see cref="TopmostThreshold"/> (bubbles, bouncing text, brain-drain, spiral, pink tint):
///    <b>Topmost</b> — the always-visible passive overlays.
///  - rank &lt; threshold (mandatory video, lock card, flash/gif, subliminal): <b>non-topmost</b>.
/// Windows always draws topmost windows above non-topmost ones, so the tint/spiral stay above the
/// churning full-screen effects with ZERO re-pinning and zero blink — no timer, no re-stacking.
/// Windows-only; a no-op elsewhere (Avalonia's <c>Topmost</c> property handles ordering there).
/// </summary>
internal static class OverlayZ
{
    /// <summary>Canonical layer ranks, bottom (low) → top (high).</summary>
    public static class Layer
    {
        public const int Video = 10;
        public const int LockCard = 20;
        public const int Flash = 30;
        public const int Subliminal = 40;
        public const int Bubbles = 45;
        public const int BouncingText = 50;
        public const int BrainDrain = 55;
        public const int Spiral = 60;
        public const int PinkTint = 70;
    }

    /// <summary>
    /// Ranks at or above this stay in the topmost band (the passive overlays + bubbles); ranks below
    /// are demoted to non-topmost so they can never cover the tint. Bubbles stay topmost so they're
    /// poppable on top and so the shared bubble window isn't demoted inside Chaos mode.
    /// </summary>
    private const int TopmostThreshold = Layer.Bubbles;

    /// <summary>Place a window once into its z-band (topmost or non-topmost). Never re-pins.</summary>
    public static void Register(Window? window, int rank)
    {
        if (window == null || !OperatingSystem.IsWindows()) return;
        bool topmost = rank >= TopmostThreshold;

        if (TryHandle(window) != IntPtr.Zero)
        {
            Apply(window, topmost);
            return;
        }

        EventHandler? onOpened = null;
        onOpened = (_, _) =>
        {
            window.Opened -= onOpened;
            Apply(window, topmost);
        };
        window.Opened += onOpened;
    }

    private static void Apply(Window window, bool topmost)
    {
        try
        {
            window.Topmost = topmost;
            var hwnd = TryHandle(window);
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    private static IntPtr TryHandle(Window w)
    {
        try { return w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero; }
        catch { return IntPtr.Zero; }
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
