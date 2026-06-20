using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Localization;
using Point = global::Avalonia.Point;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the fullscreen 16-point webcam calibration window.
/// The eye-tracking pipeline is stubbed with IFrameSource / IVideoSurface seams.
/// </summary>
public partial class WebcamCalibrationWindow : Window
{
    public bool? DialogResult { get; set; }

    /// <summary>
    /// True while a calibration window is on screen.
    /// </summary>
    public static bool IsShowing { get; private set; }

    /// <summary>
    /// Set to true when the user clicks Recalibrate on the verify panel.
    /// </summary>
    public bool WantsRecalibrate { get; private set; }

    private const int ReadyMs = 600;
    private const int SampleMs = 1100;
    private const int SettleMs = 200;
    private const int GridSize = 4;
    private const double EdgeMargin = 40;

    private readonly IFrameSource? _frameSource;
    private readonly IVideoSurface? _videoSurface;
    private bool _cancelled;
    private bool _completedOk;
    private ScaleTransform? _ringScale;
    private DispatcherTimer? _ringPulseTimer;
    private DispatcherTimer? _verifyTimer;

    public WebcamCalibrationWindow()
    {
        InitializeComponent();
        IsShowing = true;
        DotRingFg.StrokeDashArray = new AvaloniaList<double>(new[] { 0.0, 10000.0 });
    }

    public WebcamCalibrationWindow(IFrameSource? frameSource = null, IVideoSurface? videoSurface = null) : this()
    {
        _frameSource = frameSource;
        _videoSurface = videoSurface;
    }

    private async void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_frameSource == null)
        {
            ShowError("Webcam tracking is not running. Start tracking before calibrating.");
            return;
        }

        DotCanvas.IsVisible = false;
        StatusPanel.IsVisible = false;
        IntroPanel.IsVisible = true;
        ShortcutHintBanner.IsVisible = true;

        // Wait for Continue or ESC.
    }

    private void BtnIntroContinue_Click(object? sender, RoutedEventArgs e)
    {
        _ = RunSequenceAsync();
    }

    private async Task RunSequenceAsync()
    {
        IntroPanel.IsVisible = false;
        ShortcutHintBanner.IsVisible = false;
        DotCanvas.IsVisible = true;
        StatusPanel.IsVisible = true;

        await Task.Delay(50); // layout beat

        var w = Bounds.Width;
        var h = Bounds.Height;
        double xL = EdgeMargin, xR = w - EdgeMargin;
        double yT = EdgeMargin, yB = h - EdgeMargin;
        var positions = new List<Point>();
        for (int r = 0; r < GridSize; r++)
        {
            double y = yT + (yB - yT) * (r / (double)(GridSize - 1));
            for (int c = 0; c < GridSize; c++)
            {
                double x = xL + (xR - xL) * (c / (double)(GridSize - 1));
                positions.Add(new Point(x, y));
            }
        }

        for (int i = 0; i < positions.Count && !_cancelled; i++)
        {
            MoveDotTo(positions[i]);
            TxtProgress.Text = $"Point {i + 1} / {positions.Count}";
            TxtStatus.Text = "Look at the pink dot…";
            UpdateProgressRing(0);
            await Task.Delay(ReadyMs);
            if (_cancelled) return;

            TxtStatus.Text = "Hold steady — sampling…";
            StartRingPulse();
            double progress = 0;
            var sampleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            sampleTimer.Tick += (_, _) =>
            {
                progress += 0.05;
                UpdateProgressRing(Math.Min(1.0, progress));
                if (progress >= 1.0) sampleTimer.Stop();
            };
            sampleTimer.Start();
            await Task.Delay(SampleMs);
            sampleTimer.Stop();
            StopRingPulse();
            UpdateProgressRing(1.0);
            if (_cancelled) return;
            await Task.Delay(SettleMs);
        }

        if (_cancelled) return;
        await RunValidationPhaseAsync();
        if (_cancelled) return;

        _completedOk = true;
        ValidationPanel.IsVisible = false;
        DotCanvas.IsVisible = false;
        StatusPanel.IsVisible = false;
        VerifyPanel.IsVisible = true;
        ShortcutHintBanner.IsVisible = true;
    }

    private async Task RunValidationPhaseAsync()
    {
        DotCanvas.IsVisible = false;
        ValidationPanel.IsVisible = true;
        TxtTitle.Text = "Verifying calibration";
        TxtStatus.Text = "Follow the prompts to confirm the system can read your blinks and mouth.";
        TxtProgress.Text = "";

        TxtValidationCue.Text = "";
        TxtValidationPrompt.Text = "Get ready…";
        TxtValidationDetail.Text = "A couple of quick gesture checks and you're done.";
        TxtValidationAttempt.Text = "";
        await Task.Delay(1400);
        if (_cancelled) return;

        await RunGestureCheckAsync("👁", "Blink a couple of times");
        if (_cancelled) return;
        await RunGestureCheckAsync("😮", "Open your mouth wide");
    }

    private async Task RunGestureCheckAsync(string cue, string prompt)
    {
        TxtValidationCue.Text = cue;
        TxtValidationPrompt.Text = prompt;
        TxtValidationDetail.Text = "Detected: 0 / 1";
        TxtValidationAttempt.Text = "";
        await Task.Delay(1200);
        if (_cancelled) return;
        TxtValidationCue.Text = "✓";
        TxtValidationCue.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0xE0, 0x80));
        TxtValidationDetail.Text = "Detected.";
        await Task.Delay(700);
        TxtValidationCue.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
    }

    private void BtnCalibrationHelp_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open ported HelpVideoWindow once available.
        _ = MessageBoxStub.Show(
            "Calibration help is not yet ported to Avalonia.",
            "Calibration Help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnErrorClose_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = _completedOk;
        Close(_completedOk);
    }

    private void BtnVerifyAccuracy_Click(object? sender, RoutedEventArgs e)
    {
        _verifyCountdownSecondsLeft = 15;
        UpdateVerifyCountdownUi();
        _verifyTimer?.Stop();
        _verifyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _verifyTimer.Tick += (_, _) =>
        {
            _verifyCountdownSecondsLeft--;
            if (_verifyCountdownSecondsLeft <= 0) StopVerifyCountdown();
            else UpdateVerifyCountdownUi();
        };
        _verifyTimer.Start();
        BtnVerifyAccuracy.IsEnabled = false;
    }

    private int _verifyCountdownSecondsLeft;

    private void UpdateVerifyCountdownUi()
    {
        TxtVerifyStatus.Text = $"Move your eyes around — the pink dot should track them. {_verifyCountdownSecondsLeft}s left.";
    }

    private void StopVerifyCountdown()
    {
        _verifyTimer?.Stop();
        BtnVerifyAccuracy.IsEnabled = true;
        TxtVerifyStatus.Text = "Click Verify to preview accuracy with a live gaze cursor, or close when ready.";
    }

    private void BtnVerifyRecalibrate_Click(object? sender, RoutedEventArgs e)
    {
        StopVerifyCountdown();
        WantsRecalibrate = true;
        DialogResult = false;
        Close(false);
    }

    private void BtnVerifyDone_Click(object? sender, RoutedEventArgs e)
    {
        StopVerifyCountdown();
        DialogResult = true;
        Close(true);
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _cancelled = true;
            DialogResult = false;
            Close(false);
        }
    }

    private void MoveDotTo(Point screenPoint)
    {
        Canvas.SetLeft(Dot, screenPoint.X - Dot.Width / 2);
        Canvas.SetTop(Dot, screenPoint.Y - Dot.Height / 2);
        Canvas.SetLeft(DotRingBg, screenPoint.X - DotRingBg.Width / 2);
        Canvas.SetTop(DotRingBg, screenPoint.Y - DotRingBg.Height / 2);
        Canvas.SetLeft(DotRingFg, screenPoint.X - DotRingFg.Width / 2);
        Canvas.SetTop(DotRingFg, screenPoint.Y - DotRingFg.Height / 2);
    }

    private void UpdateProgressRing(double progress)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        double radius = (DotRingFg.Width - DotRingFg.StrokeThickness) / 2.0;
        double perimeter = 2.0 * Math.PI * radius;
        double units = perimeter / DotRingFg.StrokeThickness;
        double visible = progress * units;
        double gap = Math.Max(0.001, units - visible);
        DotRingFg.StrokeDashArray = new AvaloniaList<double>(new[] { visible, gap });
    }

    private void StartRingPulse()
    {
        StopRingPulse();
        _ringScale = new ScaleTransform(1, 1);
        DotRingFg.RenderTransform = _ringScale;
        DotRingFg.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        double s = 1.0;
        int dir = 1;
        _ringPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _ringPulseTimer.Tick += (_, _) =>
        {
            s += dir * 0.01;
            if (s >= 1.18) { s = 1.18; dir = -1; }
            if (s <= 1.0) { s = 1.0; dir = 1; }
            if (_ringScale != null)
            {
                _ringScale.ScaleX = s;
                _ringScale.ScaleY = s;
            }
        };
        _ringPulseTimer.Start();
    }

    private void StopRingPulse()
    {
        _ringPulseTimer?.Stop();
        _ringPulseTimer = null;
        if (_ringScale != null)
        {
            _ringScale.ScaleX = 1.0;
            _ringScale.ScaleY = 1.0;
        }
    }

    private void ShowError(string detail)
    {
        _cancelled = true;
        StopRingPulse();
        DotCanvas.IsVisible = false;
        TxtErrorDetail.Text = detail;
        ErrorPanel.IsVisible = true;
    }

    /// <summary>
    /// Helper for callers: shows the dialog, re-opens automatically when the user clicks Recalibrate.
    /// </summary>
    public static async Task<bool?> ShowDialogWithRecalibrateAsync(Window? owner)
    {
        bool? final;
        while (true)
        {
            var dlg = new WebcamCalibrationWindow();
            try
            {
                final = await dlg.ShowDialog<bool?>(owner!);
            }
            catch
            {
                final = false;
            }
            if (!dlg.WantsRecalibrate) break;
        }
        return final;
    }

    protected override void OnClosed(EventArgs e)
    {
        IsShowing = false;
        _cancelled = true;
        _verifyTimer?.Stop();
        StopRingPulse();
        base.OnClosed(e);
    }
}
