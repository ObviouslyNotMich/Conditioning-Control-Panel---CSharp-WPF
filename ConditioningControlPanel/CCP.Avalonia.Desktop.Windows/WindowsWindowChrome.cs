using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows-specific window chrome: DWM dark title bar and Avalonia client-area extension.
/// </summary>
public sealed class WindowsWindowChrome : IWindowChrome
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    public void SetDarkTitleBar(IntPtr? nativeHandle, bool dark)
    {
        if (nativeHandle is not { } handle)
            return;

        try
        {
            var attribute = Environment.OSVersion.Version.Build >= 18985
                ? DWMWA_USE_IMMERSIVE_DARK_MODE
                : DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;

            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, attribute, ref value, Marshal.SizeOf<int>());
        }
        catch
        {
            // DWM attributes are best-effort on older Windows versions.
        }
    }

    public void ExtendClientArea(object window, bool extend)
    {
        if (window is Window avaloniaWindow)
        {
            avaloniaWindow.ExtendClientAreaToDecorationsHint = extend;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
