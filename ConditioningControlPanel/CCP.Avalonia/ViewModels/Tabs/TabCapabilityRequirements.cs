using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Capability gates that a shell tab may require. Tabs whose requirements are not
/// satisfied by the current <see cref="IPlatformCapabilities"/> are hidden on mobile
/// and other limited lifetimes.
/// </summary>
public enum TabCapabilityRequirements
{
    None = 0,
    Overlays = 1,
    GlobalHotkeys = 2,
    SystemTray = 4,
    ScreenCapture = 8,
    WebView2Browser = 16,
    Desktop = 32
}

public static class TabCapabilityRequirementsExtensions
{
    /// <summary>
    /// Returns true when the current platform satisfies all of the requested capabilities.
    /// A null capability snapshot (design time) is treated as fully supported.
    /// </summary>
    public static bool IsSupported(this TabCapabilityRequirements requirements, IPlatformCapabilities? capabilities)
    {
        if (requirements == TabCapabilityRequirements.None || capabilities is null)
        {
            return true;
        }

        if ((requirements & TabCapabilityRequirements.Overlays) != 0 && !capabilities.SupportsOverlays)
        {
            return false;
        }

        if ((requirements & TabCapabilityRequirements.GlobalHotkeys) != 0 && !capabilities.SupportsGlobalHotkeys)
        {
            return false;
        }

        if ((requirements & TabCapabilityRequirements.SystemTray) != 0 && !capabilities.SupportsSystemTray)
        {
            return false;
        }

        if ((requirements & TabCapabilityRequirements.ScreenCapture) != 0 && !capabilities.SupportsScreenCapture)
        {
            return false;
        }

        if ((requirements & TabCapabilityRequirements.WebView2Browser) != 0 && !capabilities.SupportsWebView2Browser)
        {
            return false;
        }

        if ((requirements & TabCapabilityRequirements.Desktop) != 0 && !capabilities.IsDesktop)
        {
            return false;
        }

        return true;
    }
}
