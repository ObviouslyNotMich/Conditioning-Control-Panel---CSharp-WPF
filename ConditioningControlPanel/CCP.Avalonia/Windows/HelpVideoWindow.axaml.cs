using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using LibVLCSharp.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Borderless popup that plays a short muted, looping tutorial clip for a
/// <see cref="HelpContent"/> topic. Avalonia port of the WPF HelpVideoWindow.
///
/// Fail-soft: if LibVLC is unavailable or the clip is missing, the video
/// surface stays hidden but the caption and link still show.
/// </summary>
public partial class HelpVideoWindow : Window
{
    private readonly ILogger<HelpVideoWindow> _logger;


    private static HelpVideoWindow? _current;

    private readonly string? _clipPath;
    private readonly string? _fullTutorialUrl;
    private readonly string? _whatItDoes;
    private bool _captionShown;

    private MediaPlayer? _mediaPlayer;
    private Media? _media;

    public HelpVideoWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<HelpVideoWindow>>();
}

    private HelpVideoWindow(HelpContent content)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<HelpVideoWindow>>();
TxtGlyph.Text = string.IsNullOrEmpty(content.Icon) ? "?" : content.Icon;
        TxtTitle.Text = content.Title;
        Title = content.Title;

        if (!string.IsNullOrWhiteSpace(content.CaptionKey))
        {
            TxtCaption[!TextBlock.TextProperty] = new Binding($"[{content.CaptionKey}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay
            };
            TxtCaption.IsVisible = true;
            _captionShown = true;
        }

        _whatItDoes = content.WhatItDoes;
        if (!content.HasClip) ShowWhatItDoesFallback();

        _fullTutorialUrl = content.FullTutorialUrl;
        if (!string.IsNullOrWhiteSpace(_fullTutorialUrl))
        {
            BtnFullTutorial.IsVisible = true;
        }

        if (content.HasClip)
        {
            _clipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "tutorial_videos", content.ClipFile!);
        }

        Loaded += (_, _) => StartClip();
        KeyDown += OnKeyDown;
    }

    public static void Show(HelpContent content, Window? owner, bool topmost = false)
    {
        var logger = App.Services.GetRequiredService<ILogger<HelpVideoWindow>>();
        try
        {
            CloseCurrent();

            var win = new HelpVideoWindow(content)
            {
                Owner = owner,
                Topmost = topmost
            };
            _current = win;
            win.Show();
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<HelpVideoWindow>>().LogError(ex, "HelpVideoWindow: failed to open");
        }
    }

    private static void CloseCurrent()
    {
        var existing = _current;
        _current = null;
        if (existing != null)
        {
            try { existing.Close(); } catch { /* ignore */ }
        }
    }

    private void ShowWhatItDoesFallback()
    {
        if (_captionShown) return;
        if (string.IsNullOrWhiteSpace(_whatItDoes)) return;
        TxtCaption.Text = _whatItDoes;
        TxtCaption.IsVisible = true;
        _captionShown = true;
    }

    private void StartClip()
    {
        if (string.IsNullOrEmpty(_clipPath) || !File.Exists(_clipPath))
        {
            if (!string.IsNullOrEmpty(_clipPath))
                _logger?.LogWarning("HelpVideoWindow: clip not found: {Path}", _clipPath);
            ShowWhatItDoesFallback();
            return;
        }

        try
        {
            var libVLC =
App.Services?.GetService<LibVLC>();
            if (libVLC == null)
            {
                _logger?.LogWarning("HelpVideoWindow: LibVLC not available; hiding video surface");
                ShowWhatItDoesFallback();
                return;
            }

            VideoContainer.IsVisible = true;

            _mediaPlayer = new MediaPlayer(libVLC)
            {
                Mute = true,
                EnableHardwareDecoding = true
            };

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

            VideoView.MediaPlayer = _mediaPlayer;
            _media = new Media(libVLC, _clipPath, FromType.FromPath);
            _mediaPlayer.Play(_media);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "HelpVideoWindow: failed to start clip");
            try { VideoContainer.IsVisible = false; } catch { /* ignore */ }
            ShowWhatItDoesFallback();
        }
    }

    private void BtnFullTutorial_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_fullTutorialUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_fullTutorialUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "HelpVideoWindow: failed to open tutorial url {Url}", _fullTutorialUrl);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Titlebar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (ReferenceEquals(_current, this)) _current = null;

        try
        {
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
            _logger?.LogWarning(ex, "Error during HelpVideoWindow cleanup");
        }

        base.OnClosed(e);
    }
}
