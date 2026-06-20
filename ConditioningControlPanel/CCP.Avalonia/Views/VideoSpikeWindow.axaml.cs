using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Views;

public partial class VideoSpikeWindow : Window
{
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _currentMedia;

    public VideoSpikeWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new LibVLC();
            _player = new MediaPlayer(_libVlc);
            VideoView.MediaPlayer = _player;
            StatusText.Text = "LibVLC initialized. Press Play.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Init failed: {ex.Message}";
        }
    }

    private void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_player == null || _libVlc == null) return;

        var videoPath = FindSampleVideo();
        if (string.IsNullOrEmpty(videoPath))
        {
            StatusText.Text = "No sample video found.";
            return;
        }

        StopInternal();
        _currentMedia = new Media(_libVlc, videoPath);
        _player.Play(_currentMedia);
        StatusText.Text = $"Playing: {Path.GetFileName(videoPath)}";
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        StopInternal();
        StatusText.Text = "Stopped.";
    }

    private void StopInternal()
    {
        _player?.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;
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
