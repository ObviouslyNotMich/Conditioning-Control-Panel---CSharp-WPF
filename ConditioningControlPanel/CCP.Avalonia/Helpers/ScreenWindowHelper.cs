using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>
/// Helpers for sizing an Avalonia <see cref="Window"/> to cover a physical monitor.
/// </summary>
public static class ScreenWindowHelper
{
    /// <summary>
    /// Sizes <paramref name="window"/> so it exactly covers <paramref name="screen"/>.
    /// <see cref="Window.Position"/> is set in device pixels (Avalonia uses raw screen
    /// coordinates); <see cref="Window.Width"/> and <see cref="Window.Height"/> are set
    /// in device-independent pixels by dividing the physical size by the monitor's
    /// <see cref="ScreenInfo.Scaling"/>.
    /// </summary>
    public static void ConstrainToScreen(this Window window, ScreenInfo screen)
    {
        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        window.Position = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y);
        window.Width = screen.Bounds.Width / scale;
        window.Height = screen.Bounds.Height / scale;
    }
}
