using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Ocr;

/// <summary>
/// Windows-only screen OCR service for the Avalonia head. Captures each screen,
/// runs it through the Windows OCR engine, and feeds detected words into the
/// Awareness Engine keyword-trigger service.
/// </summary>
public sealed class AvaloniaScreenOcrService : IScreenOcrService, IDisposable
{
    private const int ConfirmationDelayMs = 200;
    private const int MaxQuickConfirmScans = 3;

    private readonly IKeywordTriggerService _keywordTriggerService;
    private readonly IScreenProvider _screenProvider;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AvaloniaScreenOcrService> _logger;
    private OcrEngine? _ocrEngine;
    private readonly object _lock = new();

    private Timer? _timer;
    private bool _disposed;
    private bool _isRunning;
    private bool _scanInProgress;
    private readonly object _scanLock = new();

    public bool IsRunning => _isRunning;

    public AvaloniaScreenOcrService(
        IKeywordTriggerService keywordTriggerService,
        IScreenProvider screenProvider,
        ISettingsService settingsService,
        ILogger<AvaloniaScreenOcrService> logger)
    {
        _keywordTriggerService = keywordTriggerService ?? throw new ArgumentNullException(nameof(keywordTriggerService));
        _screenProvider = screenProvider ?? throw new ArgumentNullException(nameof(screenProvider));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private OcrEngine? EnsureEngine()
    {
        if (_ocrEngine != null) return _ocrEngine;
        lock (_lock)
        {
            if (_ocrEngine != null) return _ocrEngine;
            try
            {
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_ocrEngine == null)
                    _logger.LogWarning("ScreenOcrService: No OCR language pack available");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ScreenOcrService: Failed to create OCR engine");
            }
            return _ocrEngine;
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning) return;

            var intervalMs = _settingsService.Current?.ScreenOcrIntervalMs ?? 3000;
            // Defer the heavy OCR model load until the first timer tick instead of
            // paying for it during startup. The first scan is scheduled ~10 s out so
            // the benchmark's 10 s memory snapshot stays clean.
            const int startupDelayMs = 10000;
            _timer = new Timer(OnTimerTick, null, startupDelayMs, intervalMs);
            _isRunning = true;
            _logger.LogInformation("ScreenOcrService started (interval: {Interval}ms, first scan in {Delay}ms)", intervalMs, startupDelayMs);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _timer?.Dispose();
            _timer = null;
            _isRunning = false;
            _logger.LogInformation("ScreenOcrService stopped");
        }
    }

    public void UpdateInterval(int intervalMs)
    {
        lock (_lock)
        {
            if (!_isRunning || _timer == null) return;
            _timer.Change(intervalMs, intervalMs);
        }
    }

    private void OnTimerTick(object? state)
    {
        if (_disposed || !_isRunning) return;

        var engine = EnsureEngine();
        if (engine == null) return;

        lock (_scanLock)
        {
            if (_scanInProgress) return;
            _scanInProgress = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var allWords = await ScanAllScreensAsync();
                if (_disposed) return;
                await DispatchOcrResultsAsync(allWords);

                int quickScans = 0;
                while (!_disposed
                       && _keywordTriggerService.NeedsOcrConfirmation
                       && quickScans < MaxQuickConfirmScans)
                {
                    quickScans++;
                    await Task.Delay(ConfirmationDelayMs);
                    if (_disposed) return;

                    var confirmWords = await ScanAllScreensAsync();
                    if (_disposed) return;
                    await DispatchOcrResultsAsync(confirmWords);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ScreenOcrService: Scan error");
            }
            finally
            {
                lock (_scanLock)
                {
                    _scanInProgress = false;
                }
            }
        });
    }

    private async Task<List<OcrWordHit>> ScanAllScreensAsync()
    {
        var allWords = new List<OcrWordHit>();
        var screens = _screenProvider.GetAllScreens();

        foreach (var screen in screens)
        {
            var (_, words) = await CaptureAndRecognizeAsync(screen);
            if (words != null)
                allWords.AddRange(words);
        }

        return allWords;
    }

    private async Task DispatchOcrResultsAsync(List<OcrWordHit> words)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Dispatch(words);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => Dispatch(words));
    }

    private void Dispatch(List<OcrWordHit> words)
    {
        if (_disposed) return;

        var filtered = words;
        if (_settingsService.Current?.AwarenessIgnoreOwnUi == true && words.Count > 0)
        {
            var ccpRects = GetCcpWindowRects();
            if (ccpRects.Count > 0)
            {
                filtered = new List<OcrWordHit>(words.Count);
                int dropped = 0;
                foreach (var w in words)
                {
                    bool insideCcp = ccpRects.Any(r => r.Intersects(new Rect(w.ScreenRect.X, w.ScreenRect.Y, w.ScreenRect.Width, w.ScreenRect.Height)));
                    if (!insideCcp) filtered.Add(w);
                    else dropped++;
                }

                if (dropped > 0)
                {
                    _logger.LogInformation("OCR self-exclusion: dropped {Dropped}/{Total} words inside {N} CCP rect(s)",
                        dropped, words.Count, ccpRects.Count);
                }
            }
        }

        _keywordTriggerService.CheckOcrWords(filtered);
    }

    private async Task<(string? text, List<OcrWordHit>? words)> CaptureAndRecognizeAsync(ScreenInfo screen)
    {
        Bitmap? bitmap = null;
        try
        {
            var bounds = screen.Bounds;
            bitmap = new Bitmap((int)bounds.Width, (int)bounds.Height, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen((int)bounds.X, (int)bounds.Y, 0, 0, new System.Drawing.Size((int)bounds.Width, (int)bounds.Height));
            }

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Bmp);
            ms.Position = 0;

            using var rasStream = ms.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(rasStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var result = await _ocrEngine!.RecognizeAsync(softwareBitmap);
            if (result == null)
                return (null, null);

            var words = new List<OcrWordHit>();
            foreach (var line in result.Lines)
            {
                foreach (var word in line.Words)
                {
                    var br = word.BoundingRect;
                    words.Add(new OcrWordHit
                    {
                        Text = word.Text,
                        ScreenRect = new ConditioningControlPanel.Core.Platform.PixelRect(
                            bounds.X + br.X,
                            bounds.Y + br.Y,
                            br.Width,
                            br.Height),
                        Screen = screen
                    });
                }
            }

            return (result.Text, words);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ScreenOcrService: Capture failed for {Screen}", screen.Name);
            return (null, null);
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private List<Rect> GetCcpWindowRects()
    {
        var rects = new List<Rect>();
        try
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return rects;

            foreach (var window in desktop.Windows)
            {
                try
                {
                    if (window == null || !window.IsVisible) continue;
                    var scaling = window.Screens?.ScreenFromWindow(window)?.Scaling ?? 1.0;
                    var x = window.Position.X;
                    var y = window.Position.Y;
                    var w = window.Bounds.Width * scaling;
                    var h = window.Bounds.Height * scaling;
                    rects.Add(new Rect(x, y, w, h));
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ScreenOcrService: Failed to enumerate CCP windows");
        }
        return rects;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
