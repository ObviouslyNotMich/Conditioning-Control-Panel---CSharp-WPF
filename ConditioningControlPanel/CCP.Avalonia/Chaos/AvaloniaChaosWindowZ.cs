using System;
using System.Linq;
using System.Runtime.InteropServices;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosWindowZ: shared z-order helper for chaos overlays.
/// Cross-platform topmost re-assert is limited; extended-window-style click-through
/// is platform-specific and stubbed with TODOs.
/// </summary>
internal static class AvaloniaChaosWindowZ
{
    public static bool BornTopmost => AvaloniaChaosMode.BornTopmost;

    /// <summary>Re-assert a window to the top of the topmost band without stealing focus.</summary>
    public static void RaiseTopmost(global::Avalonia.Controls.Window? w)
    {
        if (w == null) return;
        try
        {
            bool topmost = BornTopmost;
            w.Topmost = false;
            w.Topmost = topmost;
            if (OperatingSystem.IsWindows() && w.TryGetPlatformHandle() is { } handle)
            {
                var insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
                SetWindowPos(handle.Handle, insertAfter, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
        catch { }
    }

    /// <summary>Re-stack only while a mandatory video is on screen.</summary>
    public static void RaiseAboveVideo(global::Avalonia.Controls.Window? w)
    {
        if (!AvaloniaChaosEnv.VideoIsPlaying) return;
        RaiseTopmost(w);
    }

    /// <summary>
    /// Bounds (DIPs) a full-screen chaos overlay should cover. Single-monitor unless
    /// DualMonitorEnabled is true.
    /// </summary>
    public static (double left, double top, double width, double height) StageBounds(bool forcePrimary = false)
    {
        var screens = GetScreens();
        if (screens == null) return (0, 0, 1920, 1080);

        bool dual = !forcePrimary && (App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.DualMonitorEnabled ?? true);
        if (dual)
        {
            var all = screens.All;
            if (all.Count == 0) return (0, 0, 1920, 1080);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var s in all)
            {
                var b = s.Bounds;
                minX = Math.Min(minX, b.X);
                minY = Math.Min(minY, b.Y);
                maxX = Math.Max(maxX, b.Right);
                maxY = Math.Max(maxY, b.Bottom);
            }
            return (minX, minY, maxX - minX, maxY - minY);
        }

        var primary = screens.Primary;
        if (primary == null) return (0, 0, 1920, 1080);
        var pb = primary.Bounds;
        return (pb.X, pb.Y, pb.Width, pb.Height);
    }

    internal static global::Avalonia.Controls.Screens? GetScreens()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            return window.Screens;
        }
        return null;
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
