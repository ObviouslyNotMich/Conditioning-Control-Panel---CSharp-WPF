namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Platform-specific window chrome/title-bar customizations.
/// </summary>
public interface IWindowChrome
{
    void SetDarkTitleBar(IntPtr? nativeHandle, bool dark);
    void ExtendClientArea(object window, bool extend);
}
