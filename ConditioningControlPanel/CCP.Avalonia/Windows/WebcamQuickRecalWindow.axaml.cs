using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;

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
            ShowError("Eye-tracking service is not available in the Avalonia port yet.");
            return;
        }

        // TODO: wire real OnGazeMove stream once WebcamTrackingService is ported to CCP.Core.
        Dot.IsVisible = true;
        TxtStatus.Text = "Get comfortable, then look at the pink dot.";
        await Task.Delay(ReadyMs);
        TxtStatus.Text = "Hold your gaze on the dot…";
        await Task.Delay(SampleMs);

        _completedOk = true;
        TxtStatus.Text = "Done. Cursor nudged by (0, 0) px.";
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
