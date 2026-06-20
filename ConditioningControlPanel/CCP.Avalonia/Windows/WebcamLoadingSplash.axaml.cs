using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Small, movable splash shown while the webcam / eye-tracking engine starts up.
/// Avalonia port — the live progress stream is stubbed until the tracking service
/// is extracted to CCP.Core.
/// </summary>
public partial class WebcamLoadingSplash : Window
{
    private bool _closing;
    private DispatcherTimer? _pulseTimer;

    public WebcamLoadingSplash()
    {
        InitializeComponent();
        StartPulse();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Borderless window — let the user drag it anywhere.
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// Update the progress bar (0.0–1.0) and status text. Safe to call from any thread.
    /// </summary>
    public void SetProgress(double progress, string? status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_closing) return;
            if (!string.IsNullOrEmpty(status)) TxtStatus.Text = status;
            if (ProgressFill.RenderTransform is ScaleTransform st)
                st.ScaleX = Math.Clamp(progress, 0.0, 1.0);
        });
    }

    /// <summary>
    /// Show a failure message on the splash, then auto-close after a short beat.
    /// Safe to call from any thread; idempotent with CloseSplash.
    /// </summary>
    public void ShowErrorAndClose(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_closing) return;
            StopPulse();
            if (!string.IsNullOrEmpty(message)) TxtStatus.Text = message;
            ProgressFill.Opacity = 1.0;

            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2800) };
            hold.Tick += (_, _) => { hold.Stop(); CloseSplash(); };
            hold.Start();
        });
    }

    /// <summary>
    /// Fade the splash out and close it. Safe to call from any thread, and idempotent.
    /// </summary>
    public void CloseSplash()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_closing) return;
            _closing = true;
            StopPulse();

            double opacity = 1.0;
            var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            fade.Tick += (_, _) =>
            {
                opacity -= 0.1;
                Opacity = Math.Max(0, opacity);
                if (opacity <= 0)
                {
                    fade.Stop();
                    try { Close(); } catch { }
                }
            };
            fade.Start();
        });
    }

    private void StartPulse()
    {
        StopPulse();
        double opacity = 1.0;
        int direction = -1;
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _pulseTimer.Tick += (_, _) =>
        {
            opacity += direction * 0.02;
            if (opacity <= 0.55)
            {
                opacity = 0.55;
                direction = 1;
            }
            else if (opacity >= 1.0)
            {
                opacity = 1.0;
                direction = -1;
            }
            ProgressFill.Opacity = opacity;
        };
        _pulseTimer.Start();
    }

    private void StopPulse()
    {
        _pulseTimer?.Stop();
        _pulseTimer = null;
        if (ProgressFill != null) ProgressFill.Opacity = 1.0;
    }
}
