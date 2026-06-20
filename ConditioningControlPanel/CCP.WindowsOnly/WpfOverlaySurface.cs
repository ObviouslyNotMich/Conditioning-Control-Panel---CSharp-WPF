using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// WPF overlay window shim for <see cref="IOverlaySurface"/>.
/// </summary>
public sealed class WpfOverlaySurface : Window, IOverlaySurface
{
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    public WpfOverlaySurface()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
    }

    public new bool IsVisible => base.IsVisible;

    public void SetClickThrough(bool enabled)
    {
        Dispatcher.Invoke(() =>
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (enabled)
            {
                exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
            }
            else
            {
                exStyle &= ~WS_EX_TRANSPARENT;
            }
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        });
    }

    public void SetBounds(PixelRect rect)
    {
        Dispatcher.Invoke(() =>
        {
            Left = rect.X;
            Top = rect.Y;
            Width = rect.Width;
            Height = rect.Height;
        });
    }
}
