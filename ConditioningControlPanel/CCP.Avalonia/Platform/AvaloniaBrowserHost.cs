using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Browser host that opens the system default browser. Cross-platform fallback.
/// </summary>
public sealed class AvaloniaBrowserHost : IBrowserHost
{
    public Task NavigateAsync(Uri url)
    {
        OpenWithSystemDefault(url.ToString());
        return Task.CompletedTask;
    }

    private static void OpenWithSystemDefault(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Prefer explicit Windows fallbacks; a bare ShellExecute can throw when no
            // default browser is registered (Win32Exception 0x800401F5).
            if (TryStart("explorer.exe", url)) return;
            if (TryStart("cmd.exe", $"/c start \"\" \"{url}\"")) return;
            if (TryStart("rundll32.exe", $"url.dll,FileProtocolHandler {url}")) return;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (TryStart("xdg-open", url)) return;
            if (TryStart("sensible-browser", url)) return;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (TryStart("open", url)) return;
        }

        // Final platform-agnostic attempt.
        TryStart(url, useShellExecute: true);
    }

    private static bool TryStart(string fileName, string arguments)
    {
        return TryStart(fileName, arguments, false);
    }

    private static bool TryStart(string fileName, bool useShellExecute)
    {
        return TryStart(fileName, string.Empty, useShellExecute);
    }

    private static bool TryStart(string fileName, string arguments, bool useShellExecute)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = useShellExecute,
                CreateNoWindow = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<string> ExecuteScriptAsync(string script)
        => Task.FromResult(string.Empty);

    public event EventHandler<string>? TitleChanged { add { } remove { } }
    public event EventHandler<Uri>? Navigated { add { } remove { } }

    public bool IsFullscreen => false;
    public event EventHandler<bool>? FullscreenChanged { add { } remove { } }

    public Control? CreateBrowserControl() => null;
}
