namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Desktop wallpaper override. Windows-only in practice.
/// </summary>
public interface IWallpaperProvider
{
    void SetWallpaper(string? imagePath);
    void RestoreOriginalWallpaper();
}
