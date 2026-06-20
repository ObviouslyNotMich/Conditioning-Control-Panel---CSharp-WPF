using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Mobile-specific <see cref="IPlatformCapabilities"/> implementation.
/// Reports all desktop-only seams as unsupported so the UI can hide or disable
/// options that do not make sense on Android/iOS.
/// </summary>
public sealed class MobilePlatformCapabilities : IPlatformCapabilities
{
    public bool IsWindows => false;
    public bool IsLinux => false;
    public bool IsMacOS => false;
    public bool IsDesktop => false;
    public bool IsMobile => true;

    public bool SupportsGlobalHotkeys => false;
    public bool SupportsInputHooks => false;
    public bool SupportsWallpaperOverride => false;
    public bool SupportsSystemTray => false;
    public bool SupportsOverlays => false;
    public bool SupportsClickThrough => false;
    public bool SupportsWebView2Browser => false;
    public bool SupportsSingleInstance => false;
    public bool SupportsScreenCapture => false;
    public bool SupportsLibVLC => true;
}
