using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows overlay surface with click-through via Win32 WS_EX_TRANSPARENT.
/// </summary>
public sealed class WindowsOverlaySurface : AvaloniaOverlaySurface
{
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    public override void SetClickThrough(bool enabled)
    {
        if (TryGetPlatformHandle() is not { } handle)
            return;

        var exStyle = GetWindowLong(handle.Handle, GWL_EXSTYLE);
        if (enabled)
        {
            exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        }
        else
        {
            exStyle &= ~WS_EX_TRANSPARENT;
        }

        SetWindowLong(handle.Handle, GWL_EXSTYLE, exStyle);
    }
}
