using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Point = global::Avalonia.Point;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the fullscreen gaze tracker test window.
/// Uses IFrameSource / IVideoSurface platform seams as stubs until the real
/// gaze stream is available cross-platform.
/// </summary>
public partial class WebcamGazeTrackerWindow : Window
{
    private const int SmoothFrames = 5;

    private readonly IFrameSource? _frameSource;
    private readonly IVideoSurface? _videoSurface;
    private readonly System.Collections.Generic.Queue<Point> _smoothBuffer = new();
    private DispatcherTimer? _updateTimer;
    private readonly Random _random = new();

    public WebcamGazeTrackerWindow()
    {
        InitializeComponent();
    }

    public WebcamGazeTrackerWindow(IFrameSource? frameSource = null, IVideoSurface? videoSurface = null) : this()
    {
        _frameSource = frameSource;
        _videoSurface = videoSurface;
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_frameSource == null)
        {
            ShowError("Webcam tracking is not running. Start tracking before opening the tracker test.");
            return;
        }

        // TODO: replace with real OnGazeMove subscription once the tracking service is ported.
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _updateTimer.Tick += (_, _) => UpdateDotFromStub();
        _updateTimer.Start();
    }

    private void UpdateDotFromStub()
    {
        double cx = Bounds.Width / 2 + Math.Sin(_random.NextDouble() * Math.PI * 2) * 60;
        double cy = Bounds.Height / 2 + Math.Cos(_random.NextDouble() * Math.PI * 2) * 40;

        _smoothBuffer.Enqueue(new Point(cx, cy));
        while (_smoothBuffer.Count > SmoothFrames) _smoothBuffer.Dequeue();

        double sumX = 0, sumY = 0;
        foreach (var p in _smoothBuffer) { sumX += p.X; sumY += p.Y; }
        cx = sumX / _smoothBuffer.Count;
        cy = sumY / _smoothBuffer.Count;

        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double dotW = Dot.Width, dotH = Dot.Height;
        double left = Math.Max(0, Math.Min(w - dotW, cx - dotW / 2));
        double top = Math.Max(0, Math.Min(h - dotH, cy - dotH / 2));

        Canvas.SetLeft(Dot, left);
        Canvas.SetTop(Dot, top);
        Dot.IsVisible = true;
        TxtCoords.Text = $"x={cx,7:F1}  y={cy,7:F1}";
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void BtnErrorClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowError(string detail)
    {
        DotCanvas.IsVisible = false;
        TxtErrorDetail.Text = detail;
        ErrorPanel.IsVisible = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _updateTimer?.Stop();
        base.OnClosed(e);
    }
}
