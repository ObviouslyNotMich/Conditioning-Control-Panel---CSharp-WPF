using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Mobile browser host that opens URLs in the system browser.
/// Embedding a web view is head-specific on Android/iOS, so this shared mobile
/// implementation delegates to the OS launcher and exposes no browser UI surface.
/// </summary>
public sealed class MobileBrowserHost : IBrowserHost
{
    public event EventHandler<string>? TitleChanged { add { } remove { } }
    public event EventHandler<Uri>? Navigated { add { } remove { } }
    public event EventHandler<bool>? FullscreenChanged { add { } remove { } }

    public bool IsFullscreen => false;

    public Task<string> ExecuteScriptAsync(string script)
        => Task.FromResult(string.Empty);

    public async Task NavigateAsync(Uri url)
    {
        try
        {
            var topLevel = GetCurrentTopLevel();
            if (topLevel?.Launcher is { } launcher)
            {
                await launcher.LaunchUriAsync(url);
                return;
            }
        }
        catch
        {
            // If the Avalonia launcher is unavailable, fall back to the system browser.
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url.ToString(),
                UseShellExecute = true
            });
        }
        catch
        {
            // Best-effort: mobile OSs may block Process.Start, but the launcher path above
            // is the preferred route on Android/iOS.
        }
    }

    private static TopLevel? GetCurrentTopLevel()
    {
        var lifetime = Application.Current?.ApplicationLifetime;

        if (lifetime is ISingleViewApplicationLifetime single && single.MainView is { } view)
        {
            return TopLevel.GetTopLevel(view);
        }

        if (lifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } window)
        {
            return window;
        }

        return null;
    }
}
