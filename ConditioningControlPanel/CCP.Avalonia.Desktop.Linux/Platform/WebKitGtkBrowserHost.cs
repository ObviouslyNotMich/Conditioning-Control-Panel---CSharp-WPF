using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Linux stub browser host. Visual WebKitGTK embedding is not implemented yet;
/// links open in the system default browser.
/// </summary>
public sealed class WebKitGtkBrowserHost : IBrowserHost
{
    public Task NavigateAsync(Uri url)
    {
        Process.Start(new ProcessStartInfo { FileName = url.ToString(), UseShellExecute = true });
        return Task.CompletedTask;
    }

    public Task<string> ExecuteScriptAsync(string script) => Task.FromResult(string.Empty);

    public event EventHandler<string>? TitleChanged { add { } remove { } }
    public event EventHandler<Uri>? Navigated { add { } remove { } }

    public bool IsFullscreen => false;
    public event EventHandler<bool>? FullscreenChanged { add { } remove { } }

    public Control? CreateBrowserControl() => null;
}
