using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Controls;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Views;

public partial class InlineLoopSpikeWindow : Window
{
    private AvaloniaInlineLoopVideo? _loopVideo;

    public InlineLoopSpikeWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PlayButton_Click(null, null!);
    }

    private void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = FindSampleVideo();
        if (string.IsNullOrEmpty(path))
        {
            StatusText.Text = "No sample video found.";
            return;
        }

        if (_loopVideo == null)
        {
            var libVlc = App.Services.GetRequiredService<LibVLC>();
            _loopVideo = new AvaloniaInlineLoopVideo(libVlc, path, 480, 270);
            VideoHost.Child = _loopVideo.Surface;
        }

        _loopVideo.Resume();
        StatusText.Text = $"Playing: {Path.GetFileName(path)}";
    }

    private void PauseButton_Click(object? sender, RoutedEventArgs e)
    {
        _loopVideo?.Pause();
        StatusText.Text = "Paused.";
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        _loopVideo?.Dispose();
        _loopVideo = null;
        VideoHost.Child = null;
        StatusText.Text = "Disposed.";
    }

    private static string? FindSampleVideo()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "_test_loop.mp4"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources", "tutorial_videos", "_test_loop.mp4"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "tutorial_videos", "_test_loop.mp4"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
