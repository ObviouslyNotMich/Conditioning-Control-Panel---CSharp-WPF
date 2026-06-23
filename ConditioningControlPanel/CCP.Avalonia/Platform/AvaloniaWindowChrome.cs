using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform window chrome helper.
/// Windows uses DWM for dark title-bar mode; Avalonia's own
/// <see cref="Avalonia.Controls.Window.ExtendClientAreaToDecorationsHint"/>
/// handles client-area decorations on all platforms.
/// </summary>
public sealed class AvaloniaWindowChrome : IWindowChrome
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    public void SetDarkTitleBar(IntPtr? nativeHandle, bool dark)
    {
        if (!OperatingSystem.IsWindows() || !nativeHandle.HasValue || nativeHandle.Value == IntPtr.Zero)
            return;

        try
        {
            var value = dark ? 1 : 0;
            var hwnd = nativeHandle.Value;
            var size = Marshal.SizeOf<int>();

            // 20H1 and newer
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, size) != 0)
            {
                // Fall back to the older attribute value on pre-20H1 builds.
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref value, size);
            }
        }
        catch
        {
            // Best-effort; DWM may not be available on older Windows.
        }
    }

    public void ExtendClientArea(object window, bool extend)
    {
        if (window is global::Avalonia.Controls.Window avaloniaWindow)
        {
            avaloniaWindow.ExtendClientAreaToDecorationsHint = extend;
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
