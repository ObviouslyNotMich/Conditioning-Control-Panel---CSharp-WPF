using System;
using System.Runtime.InteropServices;
using System.Text;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows desktop wallpaper shim for Avalonia Windows head.
/// </summary>
public sealed class WpfWallpaperProvider : IWallpaperProvider
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string? lpvParam, int fuWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

    private string? _originalWallpaper;

    public void SetWallpaper(string? imagePath)
    {
        if (_originalWallpaper == null)
        {
            var sb = new StringBuilder(260);
            SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
            _originalWallpaper = sb.ToString();
        }

        if (!string.IsNullOrEmpty(imagePath))
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
    }

    public void RestoreOriginalWallpaper()
    {
        if (!string.IsNullOrEmpty(_originalWallpaper))
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, _originalWallpaper, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
    }
}
