namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform embedded browser host.
/// </summary>
public interface IBrowserHost
{
    Task NavigateAsync(Uri url);
    Task<string> ExecuteScriptAsync(string script);
    event EventHandler<string>? TitleChanged;
    event EventHandler<Uri>? Navigated;

    /// <summary>
    /// True when the browser is currently in fullscreen mode (e.g. HTML5 video fullscreen).
    /// </summary>
    bool IsFullscreen => false;

    /// <summary>
    /// Raised when the browser enters or exits fullscreen mode.
    /// </summary>
    event EventHandler<bool>? FullscreenChanged { add { } remove { } }

    /// <summary>
    /// Creates an Avalonia control that hosts the browser, if visual embedding is supported
    /// on the current platform. Returns null when the host uses a separate window or shell
    /// execution instead. The returned object, when non-null, is an Avalonia <see cref="Avalonia.Controls.Control"/>.
    /// </summary>
    object? CreateBrowserControl() => null;
}
