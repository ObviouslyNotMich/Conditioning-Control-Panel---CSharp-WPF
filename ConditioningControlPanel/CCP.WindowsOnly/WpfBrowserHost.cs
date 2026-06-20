using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// WebView2-backed shim for <see cref="IBrowserHost"/>.
/// </summary>
public sealed class WpfBrowserHost : IBrowserHost, IDisposable
{
    private readonly WebView2 _webView;
    private readonly string _userDataFolder;
    private CoreWebView2Environment? _environment;
    private bool _initialized;
    private bool _disposed;

    public WpfBrowserHost()
    {
        _webView = new WebView2();
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            "browser_data");
        Directory.CreateDirectory(_userDataFolder);

        _webView.CoreWebView2InitializationCompleted += OnInitializationCompleted;
    }

    /// <summary>
    /// The underlying WebView2 control. Host this in the UI tree.
    /// </summary>
    public FrameworkElement Element => _webView;

    public event EventHandler<string>? TitleChanged;
    public event EventHandler<Uri>? Navigated;

    public async Task NavigateAsync(Uri url)
    {
        await EnsureCoreAsync();
        _webView.CoreWebView2?.Navigate(url.ToString());
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        await EnsureCoreAsync();
        if (_webView.CoreWebView2 == null)
            return string.Empty;

        return await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _webView.CoreWebView2InitializationCompleted -= OnInitializationCompleted;
        _webView.Dispose();
    }

    private async Task EnsureCoreAsync()
    {
        if (_initialized || _disposed)
            return;

        if (_environment == null)
        {
            _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
        }

        await _webView.EnsureCoreWebView2Async(_environment);
    }

    private void OnInitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _webView.CoreWebView2 == null)
            return;

        _initialized = true;

        _webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
            TitleChanged?.Invoke(this, _webView.CoreWebView2.DocumentTitle);

        _webView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            if (Uri.TryCreate(_webView.CoreWebView2.Source, UriKind.Absolute, out var uri))
            {
                Navigated?.Invoke(this, uri);
            }
        };
    }
}
