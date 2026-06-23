using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;

using Animation = global::Avalonia.Animation.Animation;
using IterationCount = global::Avalonia.Animation.IterationCount;
using KeyFrame = global::Avalonia.Animation.KeyFrame;
using FillMode = global::Avalonia.Animation.FillMode;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of AchievementPopup: a borderless toast shown when an achievement
/// is unlocked.
/// </summary>
public partial class AchievementPopup : Window
{
    private readonly ILogger<AchievementPopup>? _logger;


    private readonly DispatcherTimer _autoCloseTimer;

    public AchievementPopup(Achievement achievement, string? headerIcon = null, string? headerText = null)
    {
        InitializeComponent();

        ApplyThemeShadow();

        _logger = App.Services.GetRequiredService<ILogger<AchievementPopup>>();
_logger?.LogInformation("Creating AchievementPopup for: {Name}", achievement.Name);

        TxtName.Text = achievement.Name;
        TxtFlavor.Text = achievement.FlavorText;

        if (headerIcon != null) TxtHeaderIcon.Text = headerIcon;
        if (headerText != null) TxtHeaderText.Text = headerText;

        LoadAchievementImage(achievement.ImageName);
        PositionWindow();

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            FadeOutAndClose();
        };
        _autoCloseTimer.Start();

        Opacity = 0;
        Loaded += async (s, e) =>
        {
            _logger?.LogInformation("AchievementPopup loaded, starting fade-in animation");
            await RunFadeAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        };
    }

    /// <summary>
    /// Required parameterless constructor for Avalonia designer/build.
    /// </summary>
    public AchievementPopup()
    {
        InitializeComponent();

        ApplyThemeShadow();

        _logger = App.Services.GetRequiredService<ILogger<AchievementPopup>>();
_autoCloseTimer = new DispatcherTimer();
    }

    private void ApplyThemeShadow()
    {
        if (RootBorder == null) return;
        var accent = (Application.Current?.TryFindResource("PinkColor", out var res) == true && res is Color c)
            ? c
            : Color.Parse("#FF69B4");
        RootBorder.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetX = 0, OffsetY = 0, Blur = 20, Spread = 0,
            Color = Color.FromArgb(0x80, accent.R, accent.G, accent.B)
        });
    }

    private void PositionWindow()
    {
        try
        {
            var screen = Screens.Primary;
            if (screen == null) return;

            var workArea = screen.WorkingArea;
            Position = new PixelPoint(
                (int)(workArea.X + workArea.Width - (Width * screen.Scaling) - 20),
                (int)(workArea.Y + workArea.Height - (Height * screen.Scaling) - 20));

            _logger?.LogInformation("Positioned popup at {Position}", Position);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to position achievement popup, using defaults");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void LoadAchievementImage(string imageName)
    {
        try
        {
            _logger?.LogInformation("Loading achievement image: {Name}", imageName);

            var bitmap = AvaloniaBitmapHelper.LoadResource($"achievements/{imageName}");
            if (bitmap != null)
            {
                AchievementImage.Source = bitmap;
                _logger?.LogInformation("Loaded achievement image");
            }
            else
            {
                _logger?.LogWarning("Achievement image not found: {Name}", imageName);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load achievement image: {Name}", imageName);
        }
    }

    private async Task RunFadeAnimation(double from, double to, TimeSpan duration)
    {
        var animation =
new Animation
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

    private async void FadeOutAndClose()
    {
        try
        {
            await RunFadeAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            Close();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during fade out, closing directly");
            Close();
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        FadeOutAndClose();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        FadeOutAndClose();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        base.OnClosed(e);
        _logger?.LogInformation("AchievementPopup closed");
    }
}
