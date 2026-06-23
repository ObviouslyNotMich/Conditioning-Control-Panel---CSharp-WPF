using System;
using System.Runtime.InteropServices;
using global::Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Shared Win32 helpers for chaos overlay windows on Windows.
/// </summary>
internal static class ChaosWin32Helper
{
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;

    /// <summary>
    /// Applies the overlay ex-style flags: TOOLWINDOW + NOACTIVATE + LAYERED, optionally TRANSPARENT.
    /// Call from the Opened handler (or whenever click-through toggles) so the platform handle exists.
    /// </summary>
    public static void ApplyOverlayExStyles(Window window, bool transparent)
    {
        if (window is null) return;
        if (window.TryGetPlatformHandle() is not { } handle) return;
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var ex = GetWindowLong(handle.Handle, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
            if (transparent) ex |= WS_EX_TRANSPARENT;
            else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLong(handle.Handle, GWL_EXSTYLE, ex);
        }
        catch
        {
            // Swallow; overlay styling must never break a run.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
}
