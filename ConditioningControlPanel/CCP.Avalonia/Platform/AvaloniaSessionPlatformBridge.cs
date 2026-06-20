using System.Diagnostics;
using ConditioningControlPanel.Core.Services.Sessions;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Desktop session platform bridge: opens the custom sessions folder in the OS file manager.
/// </summary>
public sealed class AvaloniaSessionPlatformBridge : ISessionPlatformBridge
{
    public void OpenCustomSessionsFolder(string folderPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", folderPath);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", folderPath);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", folderPath);
            }
            else
            {
                // Fallback: best-effort file manager launch.
                Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open custom sessions folder: {ex.Message}");
        }
    }
}
