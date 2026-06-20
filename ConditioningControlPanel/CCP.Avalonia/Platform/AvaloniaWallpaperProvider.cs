using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Desktop wallpaper override no-op. Setting the system wallpaper is platform-specific
/// and only fully supported on Windows.
/// </summary>
public sealed class AvaloniaWallpaperProvider : IWallpaperProvider
{
    public void SetWallpaper(string? imagePath)
    {
    }

    public void RestoreOriginalWallpaper()
    {
    }
}
