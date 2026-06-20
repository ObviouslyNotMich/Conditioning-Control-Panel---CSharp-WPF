using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Platform;

/// <summary>
/// Windows desktop browser host backed by WebView2.
/// </summary>
/// <remarks>
/// <para>
/// This implementation prefers embedding WebView2 directly into the Avalonia visual tree via
/// <see cref="WebView2NativeControlHost"/>.  When <see cref="CreateBrowserControl"/> is called,
/// the returned Avalonia <see cref="Control"/> can be placed anywhere (e.g. the dashboard's
/// <c>BrowserContainer</c>).
/// </para>
/// <para>
/// If the embedded control has not been created yet, <see cref="NavigateAsync(Uri)"/> falls back
/// to a separate WinForms browser window so links still open even when the dashboard control has
/// never been requested.
/// </para>
/// </remarks>
public sealed class WebView2BrowserHost : IBrowserHost, IDisposable
{
    private readonly string _userDataFolder;
    private CoreWebView2Environment? _environment;
    private WebView2NativeControlHost? _embeddedHost;
    private BrowserWindow? _popupWindow;
    private bool _disposed;

    public WebView2BrowserHost()
    {
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            "avalonia_browser_data");
        Directory.CreateDirectory(_userDataFolder);
    }

    public bool IsFullscreen { get; private set; }

    public event EventHandler<string>? TitleChanged;
    public event EventHandler<Uri>? Navigated;
    public event EventHandler<bool>? FullscreenChanged;

    /// <summary>
    /// Creates an Avalonia control that hosts WebView2.  The first call initializes the
    /// embedded WebView2 asynchronously; subsequent calls return the same control instance.
    /// </summary>
    public global::Avalonia.Controls.Control? CreateBrowserControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_embeddedHost != null)
            return _embeddedHost;

        _embeddedHost = new WebView2NativeControlHost(_environment);
        _embeddedHost.TitleChanged += (_, title) => TitleChanged?.Invoke(this, title);
        _embeddedHost.Navigated += (_, uri) => Navigated?.Invoke(this, uri);
        _embeddedHost.FullscreenChanged += (_, fullscreen) =>
        {
            IsFullscreen = fullscreen;
            FullscreenChanged?.Invoke(this, fullscreen);
        };

        return _embeddedHost;
    }

    public async Task NavigateAsync(Uri url)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Prefer the embedded control if it has already been created.
        if (_embeddedHost != null)
        {
            await EnsureEnvironmentAndInitializeEmbeddedAsync();
            if (_embeddedHost.WebView?.CoreWebView2 is { } core)
            {
                core.Navigate(url.ToString());
                return;
            }
        }

        // Embedded path unavailable: use a separate WinForms window.
        try
        {
            await EnsureBrowserWindowAsync();
        }
        catch (Exception ex)
        {
            Process.Start(new ProcessStartInfo { FileName = url.ToString(), UseShellExecute = true });
            throw new InvalidOperationException($"WebView2 is unavailable; opened the system browser instead. {ex.Message}", ex);
        }

        if (_popupWindow?.WebView.CoreWebView2 != null)
        {
            _popupWindow.WebView.CoreWebView2.Navigate(url.ToString());
        }

        _popupWindow?.Show();
        _popupWindow?.Activate();
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_embeddedHost != null)
        {
            await EnsureEnvironmentAndInitializeEmbeddedAsync();
            if (_embeddedHost.WebView?.CoreWebView2 is { } core)
                return await core.ExecuteScriptAsync(script);
        }

        await EnsureBrowserWindowAsync();

        if (_popupWindow?.WebView.CoreWebView2 == null)
            return string.Empty;

        return await _popupWindow.WebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async Task EnsureEnvironmentAndInitializeEmbeddedAsync()
    {
        if (_disposed) return;
        if (_embeddedHost == null) return;

        _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
        await _embeddedHost.EnsureInitializedAsync(_environment);
    }

    private async Task EnsureBrowserWindowAsync()
    {
        if (_popupWindow != null)
            return;

        _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
        _popupWindow = new BrowserWindow(_environment);
        WirePopupEvents();
        await _popupWindow.WebView.EnsureCoreWebView2Async(_environment);
    }

    private void WirePopupEvents()
    {
        if (_popupWindow == null) return;

        _popupWindow.WebView.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess || _popupWindow.WebView.CoreWebView2 == null)
                return;

            var core = _popupWindow.WebView.CoreWebView2;

            core.DocumentTitleChanged += (_, _) =>
                TitleChanged?.Invoke(this, core.DocumentTitle);

            core.NavigationCompleted += (_, _) =>
            {
                if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri))
                    Navigated?.Invoke(this, uri);
            };

            core.ContainsFullScreenElementChanged += (_, _) =>
            {
                IsFullscreen = core.ContainsFullScreenElement;
                _popupWindow.ApplyFullscreenState(IsFullscreen);
                FullscreenChanged?.Invoke(this, IsFullscreen);
            };

            core.NewWindowRequested += (_, e) =>
            {
                // Keep all navigation inside our single window instead of spawning extra windows.
                e.Handled = true;
                core.Navigate(e.Uri);
            };
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _embeddedHost?.Dispose();
        _embeddedHost = null;

        _popupWindow?.Dispose();
        _popupWindow = null;
    }

    private sealed class BrowserWindow : Form
    {
        private readonly FormBorderStyle _normalBorderStyle;
        private readonly FormWindowState _normalWindowState;
        private Rectangle _normalBounds;

        public WebView2 WebView { get; }

        public BrowserWindow(CoreWebView2Environment environment)
        {
            Text = "CCP Browser";
            Width = 1280;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            _normalBorderStyle = FormBorderStyle.Sizable;
            _normalWindowState = FormWindowState.Normal;
            _normalBounds = new Rectangle(0, 0, 1280, 800);

            WebView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(WebView);

            FormClosing += (_, e) =>
            {
                // Hide instead of close so the singleton host can be reused.
                e.Cancel = true;
                Hide();
            };
        }

        public void ApplyFullscreenState(bool fullscreen)
        {
            if (fullscreen)
            {
                if (WindowState != FormWindowState.Maximized || FormBorderStyle != FormBorderStyle.None)
                {
                    _normalBounds = Bounds;
                    FormBorderStyle = FormBorderStyle.None;
                    WindowState = FormWindowState.Maximized;
                }
            }
            else
            {
                FormBorderStyle = _normalBorderStyle;
                WindowState = _normalWindowState;
                Bounds = _normalBounds;
            }
        }
    }
}
