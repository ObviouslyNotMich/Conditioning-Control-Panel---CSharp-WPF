using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Window chrome stub. DWM/title-bar customization is Windows-specific; Avalonia handles
/// client-area decorations cross-platform via <see cref="Avalonia.Controls.Window"/> properties.
/// </summary>
public sealed class AvaloniaWindowChrome : IWindowChrome
{
    public void SetDarkTitleBar(IntPtr? nativeHandle, bool dark)
    {
    }

    public void ExtendClientArea(object window, bool extend)
    {
        if (window is global::Avalonia.Controls.Window avaloniaWindow)
        {
            avaloniaWindow.ExtendClientAreaToDecorationsHint = extend;
        }
    }
}
