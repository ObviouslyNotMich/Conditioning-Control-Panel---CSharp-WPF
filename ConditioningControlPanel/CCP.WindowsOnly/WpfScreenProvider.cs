using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// Windows Forms screen enumeration shim for <see cref="IScreenProvider"/>.
/// </summary>
public sealed class WpfScreenProvider : IScreenProvider
{
    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        return Screen.AllScreens.Select(ToScreenInfo).ToList();
    }

    public ScreenInfo? GetPrimaryScreen()
    {
        var primary = Screen.PrimaryScreen;
        return primary == null ? null : ToScreenInfo(primary);
    }

    public event EventHandler? ScreensChanged;

    private static ScreenInfo ToScreenInfo(Screen screen)
    {
        var bounds = screen.Bounds;
        var workingArea = screen.WorkingArea;
        return new ScreenInfo(
            screen.DeviceName,
            new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            new PixelRect(workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height),
            Scaling: 1.0);
    }
}
