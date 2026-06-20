using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Shell;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// DWM / WindowChrome shim for <see cref="IWindowChrome"/> on WPF.
/// </summary>
public sealed class WpfWindowChrome : IWindowChrome
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public void SetDarkTitleBar(IntPtr? nativeHandle, bool dark)
    {
        if (!nativeHandle.HasValue || nativeHandle.Value == IntPtr.Zero)
            return;

        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(nativeHandle.Value, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, Marshal.SizeOf(value));
    }

    public void ExtendClientArea(object window, bool extend)
    {
        if (window is not Window wpfWindow)
            return;

        if (extend)
        {
            WindowChrome.SetWindowChrome(wpfWindow, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            });
        }
        else
        {
            WindowChrome.SetWindowChrome(wpfWindow, null);
        }
    }
}
