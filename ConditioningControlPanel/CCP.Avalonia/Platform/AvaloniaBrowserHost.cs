using System.Diagnostics;
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
        var psi = new ProcessStartInfo
        {
            FileName = url.ToString(),
            UseShellExecute = true
        };
        Process.Start(psi);
        return Task.CompletedTask;
    }

    public Task<string> ExecuteScriptAsync(string script)
        => Task.FromResult(string.Empty);

    public event EventHandler<string>? TitleChanged { add { } remove { } }
    public event EventHandler<Uri>? Navigated { add { } remove { } }

    public bool IsFullscreen => false;
    public event EventHandler<bool>? FullscreenChanged { add { } remove { } }

    public Control? CreateBrowserControl() => null;
}
