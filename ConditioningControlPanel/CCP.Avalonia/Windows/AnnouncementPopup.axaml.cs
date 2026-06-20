using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Settings;

using Animation = global::Avalonia.Animation.Animation;
using IterationCount = global::Avalonia.Animation.IterationCount;
using KeyFrame = global::Avalonia.Animation.KeyFrame;
using FillMode = global::Avalonia.Animation.FillMode;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of AnnouncementPopup: a borderless popup for server-triggered
/// announcements with optional image, link, and theme support.
/// </summary>
public partial class AnnouncementPopup : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private readonly string _announcementId;
    private readonly string? _linkUrl;

    public AnnouncementPopup(string id, string title, string message, string? imageUrl,
        string? linkUrl = null, string? theme = null)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_announcementId = id;
        _linkUrl = linkUrl;
        TxtTitle.Text = title;
        TxtMessage.Text = message;

        if (!string.IsNullOrWhiteSpace(linkUrl))
        {
            BtnDownload.IsVisible = true;
        }

        if (string.Equals(theme, "matrix", StringComparison.OrdinalIgnoreCase))
        {
            ApplyMatrixTheme();
        }

        Opacity = 0;
        Loaded += async (s, e) =>
        {
            await RunFadeAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        };

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            _ = LoadImageAsync(imageUrl);
        }
    }

    /// <summary>
    /// Required parameterless constructor for Avalonia designer/build.
    /// </summary>
    public AnnouncementPopup()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_announcementId = "";
    }

    private void ApplyMatrixTheme()
    {
        var matrixGreen = Color.Parse("#00FF41");
        var matrixLightGreen = Color.Parse("#39FF14");
        var matrixBg = Color.Parse("#0D0D0D");
        var consolas = new FontFamily("Consolas, Courier New");

        OuterBorder.Background = new SolidColorBrush(Color.FromArgb(0xF0, matrixBg.R, matrixBg.G, matrixBg.B));
        OuterBorder.BorderBrush = new SolidColorBrush(matrixGreen);
        OuterBorder.BoxShadow = new BoxShadows(new BoxShadow
        {
            Color = Color.FromArgb(0x99, matrixGreen.R, matrixGreen.G, matrixGreen.B),
            Blur = 20,
            OffsetX = 0,
            OffsetY = 0,
            Spread = 0
        });

        TxtTitle.Foreground = new SolidColorBrush(matrixGreen);
        TxtTitle.FontFamily = consolas;

        TxtMessage.Foreground = new SolidColorBrush(matrixLightGreen);
        TxtMessage.FontFamily = consolas;

        if (BtnDownload.IsVisible)
        {
            BtnDownload.FontFamily = consolas;
            BtnDownload.Background = new SolidColorBrush(matrixGreen);
            BtnDownload.Foreground = Brushes.Black;
        }

        BtnDismiss.FontFamily = consolas;
        BtnDismiss.Background = new SolidColorBrush(matrixGreen);
        BtnDismiss.Foreground = Brushes.Black;

        // TODO: full matrix hover/pressed styling via ControlTheme once the app has a shared theme dictionary.
    }

    private async Task LoadImageAsync(string imageUrl)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var bytes = await httpClient.GetByteArrayAsync(imageUrl);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    AnnouncementImage.Source = new Bitmap(stream);
                    ImageContainer.IsVisible = true;
                }
                catch (Exception ex)
                {
                    _logger?.Warning(ex, "Failed to decode announcement image");
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to load announcement image from {Url}", imageUrl);
        }
    }

    private void BtnDownload_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_linkUrl) &&
            Uri.TryCreate(_linkUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed to open announcement link {Url}", _linkUrl);
            }
        }
    }

    private async Task RunFadeAnimation(double from, double to, TimeSpan duration)
    {
        var animation = new Animation
        {
            Duration = duration,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Setters = { new Setter(OpacityProperty, from) },
                    KeyTime = TimeSpan.Zero
                },
                new KeyFrame
                {
                    Setters = { new Setter(OpacityProperty, to) },
                    KeyTime = duration
                }
            }
        };
        await animation.RunAsync(this);
        Opacity = to;
    }

    private async void DismissAndClose()
    {
        var settings =
App.Services.GetService<ISettingsService>()?.Current;
        if (settings != null)
        {
            settings.DismissedAnnouncementId = _announcementId;
            App.Services.GetService<ISettingsService>()?.Save();
        }

        try
        {
            await RunFadeAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            Close();
        }
        catch
        {
            Close();
        }
    }

    private void BtnDismiss_Click(object? sender, RoutedEventArgs e)
    {
        DismissAndClose();
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        DismissAndClose();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Don't dismiss on click — announcement has action buttons, use close button or "Got it".
    }
}
