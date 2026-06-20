using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// No-op window chrome for mobile. Mobile apps render inside the OS-controlled chrome
/// and do not support custom title-bar or DWM extensions.
/// </summary>
public sealed class MobileWindowChrome : IWindowChrome
{
    public void SetDarkTitleBar(IntPtr? nativeHandle, bool dark)
    {
    }

    public void ExtendClientArea(object window, bool extend)
    {
    }
}
