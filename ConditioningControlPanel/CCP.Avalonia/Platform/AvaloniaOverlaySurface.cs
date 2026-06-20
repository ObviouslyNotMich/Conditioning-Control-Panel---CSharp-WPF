using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia overlay window shim.
/// Cross-platform click-through is not supported by Avalonia core and requires
/// platform-specific interop on the desktop heads.
/// </summary>
public class AvaloniaOverlaySurface : Window, IOverlaySurface
{
    public AvaloniaOverlaySurface()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
    }

    public new bool IsVisible => base.IsVisible;

    public virtual void SetClickThrough(bool enabled)
    {
        // Cross-platform input passthrough is not available in Avalonia core.
        // Platform heads can subclass and override this with native interop.
    }

    public void SetBounds(ConditioningControlPanel.Core.Platform.PixelRect rect)
    {
        Position = new PixelPoint((int)rect.X, (int)rect.Y);
        Width = rect.Width;
        Height = rect.Height;
    }
}
