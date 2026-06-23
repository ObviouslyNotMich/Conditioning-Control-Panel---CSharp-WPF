using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform desktop wallpaper override.
/// Windows uses the native <c>SystemParametersInfo</c> SPI.
/// Linux falls back to <c>gsettings</c> (GNOME) and then <c>feh</c>.
/// macOS uses an <c>osascript</c> Finder command.
/// Unsupported platforms silently ignore the request.
/// </summary>
public sealed class AvaloniaWallpaperProvider : IWallpaperProvider
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, string pvParam, int fWinIni);

    public void SetWallpaper(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, Path.GetFullPath(imagePath), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                return;
            }

            if (OperatingSystem.IsLinux())
            {
                SetLinuxWallpaper(imagePath);
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                SetMacWallpaper(imagePath);
            }
        }
        catch
        {
            // Best-effort; wallpaper override is not critical to app function.
        }
    }

    public void RestoreOriginalWallpaper()
    {
        // No original wallpaper is cached by this provider. The higher-level
        // wallpaper service can re-call SetWallpaper with the saved original path.
    }

    private static void SetLinuxWallpaper(string imagePath)
    {
        var fullPath = Path.GetFullPath(imagePath);
        var uri = "file://" + fullPath.Replace(" ", "%20");

        // GNOME / gsettings
        try
        {
            using var gsettings = Process.Start(new ProcessStartInfo("gsettings", $"set org.gnome.desktop.background picture-uri \"{uri}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            gsettings?.WaitForExit(2000);
            if (gsettings?.ExitCode == 0) return;
        }
        catch { /* ignore, try next */ }

        // feh fallback
        try
        {
            using var feh = Process.Start(new ProcessStartInfo("feh", $"--bg-scale \"{fullPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            feh?.WaitForExit(2000);
        }
        catch { /* ignore */ }
    }

    private static void SetMacWallpaper(string imagePath)
    {
        var script = $"tell application \"Finder\" to set desktop picture to POSIX file \"{imagePath.Replace("\"", "\\\"")}\"";
        try
        {
            using var osascript = Process.Start(new ProcessStartInfo("osascript", $"-e '{script}'")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            osascript?.WaitForExit(2000);
        }
        catch { /* ignore */ }
    }
}
