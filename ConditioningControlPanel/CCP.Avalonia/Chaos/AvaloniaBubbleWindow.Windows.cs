using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Chaos;

public sealed partial class AvaloniaBubbleWindow
{
    private const int GwlExStyle = -20;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExLayered = 0x00080000;

    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private partial void ApplyPlatformStyles()
    {
        // TODO: full per-pixel alpha click-through for the circular bubble requires layered-window
        // interop (UpdateLayeredWindow). For Stage 1 we keep the window clickable and only apply
        // tool-window / no-activate styles so it does not steal focus or show in the taskbar.
        if (!OperatingSystem.IsWindows()) return;

        var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        var exStyle = (uint)GetWindowLong(handle, GwlExStyle);
        exStyle |= WsExToolWindow | WsExNoActivate | WsExLayered;
        // WsExTransparent is intentionally omitted here so clicks still reach the bubble visual.
        // SetWindowLong(handle, GwlExStyle, new IntPtr((nint)exStyle));
        _ = SetWindowLong(handle, GwlExStyle, new IntPtr((nint)exStyle));
        _ = SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLong64(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 4 ? SetWindowLong32(hWnd, nIndex, dwNewLong) : SetWindowLong64(hWnd, nIndex, dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);
}
