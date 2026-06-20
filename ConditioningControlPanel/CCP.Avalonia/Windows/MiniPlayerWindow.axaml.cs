using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using LibVLCSharp.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the preview mini-player. Uses LibVLC for video and static image
/// fallback for GIFs (animated GIF playback is TODO).
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpeg", ".mpg", ".3gp" };
    private static readonly string[] GifExtensions = { ".gif" };

    private MediaPlayer? _mediaPlayer;
    private Media? _media;
    private DispatcherTimer? _positionTimer;
    private bool _isDraggingSlider;
    private bool _isPlaying;
    private string? _currentFilePath;

    public MiniPlayerWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
}

    public void LoadFile(string filePath)
    {
        _currentFilePath = filePath;
        var fileName = Path.GetFileName(filePath);
        TxtFileName.Text = fileName;
        Title = $"Preview - {fileName}";

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (IsVideoFile(extension))
        {
            LoadVideo(filePath);
        }
        else if (IsGifFile(extension))
        {
            LoadGif(filePath);
        }
        else
        {
            LoadImage(filePath);
        }
    }

    private static bool IsVideoFile(string extension)
    {
        return Array.Exists(VideoExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGifFile(string extension)
    {
        return Array.Exists(GifExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadVideo(string filePath)
    {
        try
        {
            LoadingOverlay.IsVisible = true;

            var libVLC = App.Services?.GetService<LibVLC>();
            if (libVLC == null)
            {
                MessageBoxStub.Show(
                    Loc.Get("msg_video_playback_not_available_libvlc_not_initi"),
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Close();
                return;
            }

            VideoContainer.IsVisible = true;

            _mediaPlayer = new MediaPlayer(libVLC)
            {
                Mute = true,
                EnableHardwareDecoding = true
            };

            _mediaPlayer.Playing += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                _isPlaying = true;
                BtnPlayPause.Content = "⏸";
                LoadingOverlay.IsVisible = false;
            });

            _mediaPlayer.Paused += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                _isPlaying = false;
                BtnPlayPause.Content = "▶";
            });

            _mediaPlayer.EndReached += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_mediaPlayer != null && _media != null)
                    {
                        _mediaPlayer.Stop();
                        _mediaPlayer.Play(_media);
                    }
                });
            };

            _mediaPlayer.LengthChanged += (_, _) => Dispatcher.UIThread.Post(UpdateTimeDisplay);

            VideoView.MediaPlayer = _mediaPlayer;
            _media = new Media(libVLC, filePath, FromType.FromPath);
            _mediaPlayer.Play(_media);

            VideoControls.IsVisible = true;

            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _positionTimer.Tick += PositionTimer_Tick;
            _positionTimer.Start();
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "MiniPlayerWindow: Failed to load video");
            MessageBoxStub.Show(
                $"Failed to load video: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void LoadGif(string filePath)
    {
        try
        {
            // TODO: animated GIF support once a cross-platform GIF renderer is available.
            _logger?.Warning("MiniPlayerWindow: animated GIF playback not yet ported; falling back to static image");
            LoadImage(filePath);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "MiniPlayerWindow: Failed to load GIF");
            LoadImage(filePath);
        }
    }

    private void LoadImage(string filePath)
    {
        try
        {
            ImagePreview.IsVisible = true;
            VideoControls.IsVisible = false;

            var bitmap = new Bitmap(filePath);
            ImagePreview.Source = bitmap;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "MiniPlayerWindow: Failed to load image");
            MessageBoxStub.Show(
                $"Failed to load image: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_mediaPlayer == null || _isDraggingSlider) return;

        SeekSlider.Value = _mediaPlayer.Position * 100;
        UpdateTimeDisplay();
    }

    private void UpdateTimeDisplay()
    {
        if (_mediaPlayer == null) return;

        var currentMs = _mediaPlayer.Time;
        var totalMs = _mediaPlayer.Length;

        var current = TimeSpan.FromMilliseconds(Math.Max(0, currentMs));
        var total = TimeSpan.FromMilliseconds(Math.Max(0, totalMs));

        TxtTime.Text = $"{current:mm\\:ss} / {total:mm\\:ss}";
    }

    private void BtnPlayPause_Click(object? sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (_mediaPlayer == null) return;

        if (_isPlaying)
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
        }
    }

    private void SeekSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void SeekSlider_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDraggingSlider = false;
        SeekToSliderPosition();
    }

    private void SeekSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isDraggingSlider && _mediaPlayer != null)
        {
            UpdateTimeDisplay();
        }
    }

    private void SeekToSliderPosition()
    {
        if (_mediaPlayer == null) return;

        var position =
(float)(SeekSlider.Value / 100.0);
        _mediaPlayer.Position = Math.Clamp(position, 0f, 1f);
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Space:
                TogglePlayPause();
                e.Handled = true;
                break;
            case Key.Left:
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 5000);
                }
                e.Handled = true;
                break;
            case Key.Right:
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Time = Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + 5000);
                }
                e.Handled = true;
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _positionTimer?.Stop();
            _positionTimer = null;

            if (VideoView != null)
            {
                VideoView.MediaPlayer = null;
            }

            if (_mediaPlayer != null)
            {
                try
                {
                    if (_mediaPlayer.IsPlaying)
                    {
                        _mediaPlayer.Stop();
                    }
                }
                catch { /* Ignore stop errors */ }

                try
                {
                    _mediaPlayer.Dispose();
                }
                catch { /* Ignore dispose errors */ }

                _mediaPlayer = null;
            }

            try
            {
                _media?.Dispose();
            }
            catch { /* Ignore dispose errors */ }
            _media = null;
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Error during MiniPlayerWindow cleanup");
        }

        base.OnClosed(e);
    }
}
