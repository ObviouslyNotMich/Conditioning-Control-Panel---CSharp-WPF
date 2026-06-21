using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the one-dot quick recalibration window.
/// The live gaze service is stubbed with IFrameSource / IVideoSurface platform seams.
/// </summary>
public partial class WebcamQuickRecalWindow : Window
{
    public bool? DialogResult { get; set; }

    private const int ReadyMs = 600;
    private const int SampleMs = 2000;
    private const int FinishHoldMs = 350;

    private readonly IFrameSource? _frameSource;
    private readonly IVideoSurface? _videoSurface;
    private bool _completedOk;

    public WebcamQuickRecalWindow()
    {
        InitializeComponent();
    }

    public WebcamQuickRecalWindow(IFrameSource? frameSource = null, IVideoSurface? videoSurface = null) : this()
    {
        _frameSource = frameSource;
        _videoSurface = videoSurface;
    }

    private async void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_frameSource == null)
        {
            ShowError(Loc.Get("window_webcam_quick_recal_service_unavailable_error"));
            return;
        }

        // TODO: wire real OnGazeMove stream once WebcamTrackingService is ported to CCP.Core.
        Dot.IsVisible = true;
        TxtStatus.Text = Loc.Get("window_webcam_quick_recal_get_comfortable_then_look_pink_dot_text");
        await Task.Delay(ReadyMs);
        TxtStatus.Text = Loc.Get("window_webcam_quick_recal_hold_gaze_status");
        await Task.Delay(SampleMs);

        _completedOk = true;
        TxtStatus.Text = Loc.Get("window_webcam_quick_recal_done_nudged_status");
        await Task.Delay(FinishHoldMs);
        DialogResult = true;
        Close(true);
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close(false);
        }
    }

    private void BtnErrorClose_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = _completedOk;
        Close(_completedOk);
    }

    private void ShowError(string detail)
    {
        Dot.IsVisible = false;
        TxtErrorDetail.Text = detail;
        ErrorPanel.IsVisible = true;
    }
}
