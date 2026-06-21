using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Deeper;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Browser preview + audio waveform cache for the Deeper editor.
/// On Windows the WebView2 browser host supplies an embedded control; on other
/// platforms it falls back to opening the system default browser.
/// </summary>
public partial class DeeperEditorWindow
{
    private IBrowserHost? _browserHost;
    private AudioWaveformCache? _waveformCache;
    private IAppEnvironment? _environment;
    private Control? _browserControl;
    private AudioWaveformResult? _waveformData;

    private void InitializePreview()
    {
        try
        {
            _browserHost = App.Services?.GetService<IBrowserHost>();
            _waveformCache = App.Services?.GetService<AudioWaveformCache>();
            _environment = App.Services?.GetService<IAppEnvironment>();
        }
        catch { /* design-time safety */ }

        if (_browserHost != null)
        {
            try
            {
                var control = _browserHost.CreateBrowserControl() as Control;
                if (control != null)
                {
                    _browserControl = control;
                    BrowserPreviewContainer.Content = control;
                    TxtBrowserPlaceholder.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                TxtBrowserPlaceholder.Text = $"Browser preview unavailable: {ex.Message}";
            }
        }

        RefreshPreviewSource();
        _ = TryAutoLoadWaveformAsync();
    }

    private void RefreshPreviewSource()
    {
        if (TxtPreviewSource == null) return;
        var source = _enhancement.MediaSource ?? "";
        TxtPreviewSource.Text = string.IsNullOrEmpty(source)
            ? "No media source set"
            : $"Source: {UrlSafety.RedactUrl(source)}";
    }

    private void TxtMetaMediaSource_LostFocus(object? sender, RoutedEventArgs e)
    {
        CommitMetadata();
        RefreshPreviewSource();
        _ = TryNavigateBrowserPreviewAsync();
        _ = TryAutoLoadWaveformAsync();
    }

    private async void BtnOpenBrowser_Click(object? sender, RoutedEventArgs e)
    {
        await TryOpenBrowserAsync(forceExternal: false);
    }

    private async void BtnLoadWaveform_Click(object? sender, RoutedEventArgs e)
    {
        await TryAutoLoadWaveformAsync();
    }

    private async Task TryOpenBrowserAsync(bool forceExternal)
    {
        if (_browserHost == null) return;
        var source = _enhancement.MediaSource;
        if (!TryGetAllowedPreviewUri(source, out var uri)) return;

        try
        {
            if (forceExternal || _browserControl == null)
            {
                await _browserHost.NavigateAsync(uri);
            }
            else
            {
                await _browserHost.NavigateAsync(uri);
            }
        }
        catch (Exception ex)
        {
            TxtBrowserPlaceholder.Text = $"Could not open preview: {ex.Message}";
            TxtBrowserPlaceholder.IsVisible = true;
        }
    }

    private async Task TryNavigateBrowserPreviewAsync()
    {
        if (_browserHost == null || _browserControl == null) return;
        var source = _enhancement.MediaSource;
        if (!TryGetAllowedPreviewUri(source, out var uri)) return;

        try
        {
            await _browserHost.NavigateAsync(uri);
            TxtBrowserPlaceholder.IsVisible = false;
        }
        catch (Exception ex)
        {
            TxtBrowserPlaceholder.Text = $"Preview navigation failed: {ex.Message}";
            TxtBrowserPlaceholder.IsVisible = true;
        }
    }

    private static bool TryGetAllowedPreviewUri(string? source, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(source)) return false;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (!UrlSafety.HostMatches(parsed, DeeperConfig.PreviewHostAllowlist)) return false;
        uri = parsed;
        return true;
    }

    private async Task TryAutoLoadWaveformAsync()
    {
        if (_waveformCache == null || WaveformCanvas == null) return;

        var path = ResolveLocalAudioPath(_enhancement.MediaSource);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            _waveformData = await _waveformCache.LoadAsync(path);
            RebuildWaveform();
        }
        catch (Exception ex)
        {
            // Leave the waveform blank on failure; don't block the editor.
            _waveformData = new AudioWaveformResult { Peaks = new float[64], DurationSeconds = 0 };
            RebuildWaveform();
        }
    }

    private string? ResolveLocalAudioPath(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        if (UrlSafety.IsSafeLocalAbsolute(source) && File.Exists(source))
            return source;

        if (_environment != null && UrlSafety.TryResolveLocalPath(source, _environment.EffectiveAssetsPath, out var resolved))
        {
            if (File.Exists(resolved)) return resolved;
        }

        // Treat a plain relative path as relative to the enhancement file's directory.
        if (!string.IsNullOrEmpty(_filePath))
        {
            var dir = System.IO.Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                var local = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, source));
                if (File.Exists(local)) return local;
            }
        }

        return null;
    }

    private void WaveformCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        RebuildWaveform();
    }

    private void RebuildWaveform()
    {
        if (WaveformCanvas == null) return;
        WaveformCanvas.Children.Clear();

        var peaks = _waveformData?.Peaks;
        if (peaks == null || peaks.Length == 0) return;

        var w = WaveformCanvas.Bounds.Width;
        var h = WaveformCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var accent = this.FindResource("DeeperAccentBrush") as IBrush ?? new SolidColorBrush(Color.Parse("#FF7B5CFF"));
        var barWidth = Math.Max(1.0, w / peaks.Length);

        for (int i = 0; i < peaks.Length; i++)
        {
            var value = Math.Clamp(peaks[i], 0, 1);
            if (value <= 0.001) continue;

            var x = i * (w / peaks.Length);
            var barHeight = value * h;
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                Fill = accent,
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, (h - barHeight) / 2);
            WaveformCanvas.Children.Add(rect);
        }
    }
}
