using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Platform;

/// <summary>
/// Avalonia <see cref="NativeControlHost"/> that embeds a WebView2 WinForms control.
/// The Win32 HWND owned by a WinForms <see cref="Panel"/> is reparented into the
/// Avalonia window by <see cref="NativeControlHost"/>; WebView2 fills the panel.
/// </summary>
/// <remarks>
/// The underlying WinForms panel and WebView2 are created once and reused when this
/// control is reparented (e.g. into an HTML5 fullscreen window). They are only disposed
/// when <see cref="Dispose"/> is called.
/// </remarks>
public sealed class WebView2NativeControlHost : NativeControlHost, IDisposable
{
    private readonly CoreWebView2Environment? _environment;
    private System.Windows.Forms.Panel? _panel;
    private WebView2? _webView;
    private Task? _initTask;

    public WebView2NativeControlHost(CoreWebView2Environment? environment = null)
    {
        _environment = environment;
    }

    /// <summary>
    /// The underlying WebView2 control, or null before <see cref="CreateNativeControlCore"/> is called.
    /// </summary>
    public WebView2? WebView => _webView;

    /// <summary>
    /// Raised when the browser document title changes.
    /// </summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>
    /// Raised when the browser finishes navigating to a new URI.
    /// </summary>
    public event EventHandler<Uri>? Navigated;

    /// <summary>
    /// Raised when a video or other element enters/exits fullscreen.
    /// </summary>
    public event EventHandler<bool>? FullscreenChanged;

    /// <summary>
    /// Ensures the WebView2 core is initialized with the supplied environment.
    /// Safe to call multiple times; subsequent calls return the same task.
    /// </summary>
    public async Task EnsureInitializedAsync(CoreWebView2Environment environment)
    {
        if (_initTask != null)
        {
            await _initTask;
            return;
        }

        _initTask = InitializeCoreAsync(environment);
        await _initTask;
    }

    private async Task InitializeCoreAsync(CoreWebView2Environment environment)
    {
        EnsurePanelAndWebViewCreated();

        if (_webView?.CoreWebView2 != null)
            return;

        try
        {
            await _webView!.EnsureCoreWebView2Async(environment);
            WireEvents(_webView.CoreWebView2);
        }
        catch (Exception)
        {
            // Initialization failures are left to consumers to surface; the control
            // remains usable as a placeholder so Avalonia layout does not break.
            throw;
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        EnsurePanelAndWebViewCreated();

        // Accessing Handle forces creation of the Win32 HWND.  NativeControlHost will
        // reparent this HWND into the Avalonia window and keep it sized to this control.
        var handle = _panel!.Handle;
        return new PlatformHandle(handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // Intentionally no-op: the panel and WebView2 are owned by this instance and
        // must survive reparenting into a fullscreen window. They are released in Dispose().
    }

    private void EnsurePanelAndWebViewCreated()
    {
        if (_panel != null)
            return;

        _panel = new System.Windows.Forms.Panel();
        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        _panel.Controls.Add(_webView);

        // Force HWND creation immediately so the handle is stable across reparenting.
        _ = _panel.Handle;
    }

    private void WireEvents(CoreWebView2? core)
    {
        if (core == null) return;

        core.DocumentTitleChanged += OnDocumentTitleChanged;
        core.NavigationCompleted += OnNavigationCompleted;
        core.ContainsFullScreenElementChanged += OnContainsFullScreenElementChanged;
        core.NewWindowRequested += OnNewWindowRequested;
    }

    private void UnwireEvents(CoreWebView2? core)
    {
        if (core == null) return;

        core.DocumentTitleChanged -= OnDocumentTitleChanged;
        core.NavigationCompleted -= OnNavigationCompleted;
        core.ContainsFullScreenElementChanged -= OnContainsFullScreenElementChanged;
        core.NewWindowRequested -= OnNewWindowRequested;
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        if (_webView?.CoreWebView2 is { } core)
            TitleChanged?.Invoke(this, core.DocumentTitle);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_webView?.CoreWebView2 is { } core &&
            Uri.TryCreate(core.Source, UriKind.Absolute, out var uri))
        {
            Navigated?.Invoke(this, uri);
        }
    }

    private void OnContainsFullScreenElementChanged(object? sender, object e)
    {
        if (_webView?.CoreWebView2 is { } core)
            FullscreenChanged?.Invoke(this, core.ContainsFullScreenElement);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Keep all navigation inside the embedded browser instead of spawning extra windows.
        e.Handled = true;
        if (_webView?.CoreWebView2 is { } core)
            core.Navigate(e.Uri);
    }

    public void Dispose()
    {
        if (_webView != null)
        {
            UnwireEvents(_webView.CoreWebView2);
            _webView.Dispose();
            _webView = null;
        }

        _panel?.Dispose();
        _panel = null;
    }
}
