using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ConditioningControlPanel;

/// <summary>
/// Shared z-order helper for the chaos layer's keep-alive windows. These windows are born
/// Topmost once and then only hide/unhide (the render-deadlock contract: no layered-window
/// churn mid-run) — but un-hiding does NOT move a window in the z-order, so once a mandatory
/// video (a fresh fullscreen topmost window) lands on top, a re-shown overlay stays buried
/// behind it for the video's whole duration. Two callers fix that:
///  - each overlay's show/unhide path calls <see cref="RaiseAboveVideo"/> so anything that
///    first draws mid-video pops above it, and
///  - ChaosModeService.RaiseGameLayerAboveVideo lifts the whole layer when a video starts
///    or is clicked (a click activates the video window at the Win32 level — WPF's
///    e.Handled only stops the routed event — which yanks it back above everything).
/// </summary>
internal static class ChaosWindowZ
{
    /// <summary>Re-assert a window to the top of the topmost band without stealing focus.</summary>
    public static void RaiseTopmost(Window? w)
    {
        if (w == null) return;
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>
    /// <see cref="RaiseTopmost"/>, but only while a mandatory video is on screen — the only
    /// time an unhidden keep-alive window can find itself buried. No-op otherwise.
    /// </summary>
    public static void RaiseAboveVideo(Window? w)
    {
        if (App.Video?.IsPlaying != true) return;
        RaiseTopmost(w);
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
