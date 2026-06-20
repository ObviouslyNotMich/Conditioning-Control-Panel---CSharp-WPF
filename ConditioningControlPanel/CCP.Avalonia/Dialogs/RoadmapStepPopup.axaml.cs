using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Roadmap;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Popup window shown when a roadmap step is completed.
/// </summary>
public partial class RoadmapStepPopup : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger? _logger;


    private readonly DispatcherTimer _autoCloseTimer;
    private readonly DispatcherTimer _fadeTimer = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly IRoadmapService _roadmap;

    public RoadmapStepPopup()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_roadmap = App.Services.GetRequiredService<IRoadmapService>();
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            FadeOutAndClose();
        };
    }

    public RoadmapStepPopup(RoadmapStepDefinition stepDef, RoadmapStepProgress progress) : this()
    {
        _logger?.Information("Creating RoadmapStepPopup for: {Title}", stepDef.Title);

        TxtStepTitle.Text = stepDef.Title;

        var trackDef = RoadmapTrackDefinition.GetByTrack(stepDef.Track);
        TxtTrackName.Text = trackDef != null
            ? $"{trackDef.Name} - {trackDef.Subtitle}"
            : stepDef.Track.ToString();

        LoadPhotoThumbnail(progress);
        PositionWindow();

        _autoCloseTimer.Start();

        Opacity = 0;
        Loaded += (s, e) =>
        {
            _logger?.Information("RoadmapStepPopup loaded, starting fade-in animation");
            _ = FadeAsync(0, 1, TimeSpan.FromMilliseconds(300));
        };
    }

    /// <summary>
    /// Position the window in the bottom-right corner of the primary screen.
    /// </summary>
    private void PositionWindow()
    {
        try
        {
            IScreenProvider? provider = null;
            try
            {
                provider = global::ConditioningControlPanel.Avalonia.App.Services?.GetService(typeof(IScreenProvider)) as IScreenProvider;
            }
            catch { /* App services may not be initialized in design/tests */ }

            var screen = provider?.GetPrimaryScreen();
            if (screen != null)
            {
                var workArea = screen.WorkingArea;
                Position = new PixelPoint(
                    (int)(workArea.X + workArea.Width - Width - 20),
                    (int)(workArea.Y + workArea.Height - Height - 20));
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            _logger?.Information("Positioned popup at {Position}", Position);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to position roadmap popup, using defaults");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void LoadPhotoThumbnail(RoadmapStepProgress progress)
    {
        try
        {
            if (string.IsNullOrEmpty(progress.PhotoPath)) return;

            var fullPath = _roadmap.GetFullPhotoPath(progress.PhotoPath);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return;

            _logger?.Information("Loading step photo thumbnail: {Path}", fullPath);

            using var stream = File.OpenRead(fullPath);
            ImgPhoto.Source = new Bitmap(stream);
            ImgPhoto.IsVisible = true;
            CheckmarkIcon.IsVisible = false;

            _logger?.Information("Photo thumbnail loaded successfully");
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to load step photo thumbnail");
            // Keep showing checkmark icon.
        }
    }

    private async System.Threading.Tasks.Task FadeAsync(double from, double to, TimeSpan duration)
    {
        if (_cts.IsCancellationRequested) return;

        var start = DateTime.UtcNow;
        Opacity = from;

        var tcs = new System.Threading.Tasks.TaskCompletionSource();
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(16);
        _fadeTimer.Tick += OnTick;
        _fadeTimer.Start();

        void OnTick(object? s, EventArgs e)
        {
            if (_cts.IsCancellationRequested)
            {
                _fadeTimer.Stop();
                _fadeTimer.Tick -= OnTick;
                tcs.TrySetCanceled();
                return;
            }

            var elapsed = DateTime.UtcNow - start;
            var t =
Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            Opacity = from + (to - from) * t;

            if (t >= 1)
            {
                _fadeTimer.Stop();
                _fadeTimer.Tick -= OnTick;
                tcs.TrySetResult();
            }
        }

        await tcs.Task;
    }

    private void FadeOutAndClose()
    {
        _ = FadeOutAndCloseAsync();
    }

    private async System.Threading.Tasks.Task FadeOutAndCloseAsync()
    {
        try
        {
            await FadeAsync(1, 0, TimeSpan.FromMilliseconds(300));
            try { Close(); } catch { /* Ignore close errors */ }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error during fade out, closing directly");
            try { Close(); } catch { }
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        FadeOutAndClose();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            FadeOutAndClose();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        _fadeTimer.Stop();
        _cts.Cancel();
        base.OnClosed(e);
        _logger?.Information("RoadmapStepPopup closed");
    }
}
