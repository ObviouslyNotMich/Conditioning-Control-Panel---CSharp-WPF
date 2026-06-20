namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Describes what the current platform/head is able to do. Used by the Avalonia
/// UI to disable or hide options that are not supported on Linux, macOS, or mobile.
/// </summary>
public interface IPlatformCapabilities
{
    // OS family
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsMacOS { get; }

    // Form factor
    bool IsDesktop { get; }
    bool IsMobile { get; }

    // Platform seams
    bool SupportsGlobalHotkeys { get; }
    bool SupportsInputHooks { get; }
    bool SupportsWallpaperOverride { get; }
    bool SupportsSystemTray { get; }
    bool SupportsOverlays { get; }
    bool SupportsClickThrough { get; }
    bool SupportsWebView2Browser { get; }
    bool SupportsSingleInstance { get; }
    bool SupportsScreenCapture { get; }
    bool SupportsLibVLC { get; }
}
