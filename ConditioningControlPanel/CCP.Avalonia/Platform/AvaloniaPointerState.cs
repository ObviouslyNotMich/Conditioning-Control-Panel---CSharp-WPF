using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Win32 global cursor + mouse-button sampling for the Avalonia head.
/// Non-Windows platforms degrade to null/false.
/// </summary>
public sealed class AvaloniaPointerState : IPointerState
{
    public System.Drawing.Point? GetCursorPosition()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return GetCursorPos(out var pt) ? new System.Drawing.Point(pt.X, pt.Y) : null;
    }

    public bool IsMouseButtonPressed(MouseButton button)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var vk = button switch
        {
            MouseButton.Left => 0x01,
            MouseButton.Right => 0x02,
            MouseButton.Middle => 0x04,
            MouseButton.XButton1 => 0x05,
            MouseButton.XButton2 => 0x06,
            _ => 0x00
        };
        if (vk == 0) return false;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
