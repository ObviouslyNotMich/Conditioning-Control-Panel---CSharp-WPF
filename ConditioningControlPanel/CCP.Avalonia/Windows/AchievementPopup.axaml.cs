using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Models;

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
    private readonly global::ConditioningControlPanel.IAppLogger? _logger;


    private readonly DispatcherTimer _autoCloseTimer;

    public AchievementPopup(Achievement achievement, string? headerIcon = null, string? headerText = null)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_logger?.Information("Creating AchievementPopup for: {Name}", achievement.Name);

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
            _logger?.Information("AchievementPopup loaded, starting fade-in animation");
            await RunFadeAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        };
    }

    /// <summary>
    /// Required parameterless constructor for Avalonia designer/build.
    /// </summary>
    public AchievementPopup()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_autoCloseTimer = new DispatcherTimer();
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

            _logger?.Information("Positioned popup at {Position}", Position);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to position achievement popup, using defaults");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void LoadAchievementImage(string imageName)
    {
        try
        {
            _logger?.Information("Loading achievement image: {Name}", imageName);

            var imagePath = Path.Combine(AppContext.BaseDirectory, "Resources", "achievements", imageName);
            if (File.Exists(imagePath))
            {
                _logger?.Information("Found image file at: {Path}", imagePath);
                AchievementImage.Source = new Bitmap(imagePath);
                return;
            }

            // TODO: mod override resolution once IModService exposes installed mod paths.
            var packUri = $"pack://application:,,,/Resources/achievements/{imageName}";
            var avares = $"avares://CCP.Avalonia/Assets/achievements/{imageName}";
            try
            {
                using var stream = AssetLoader.Open(new Uri(avares));
AchievementImage.Source = new Bitmap(stream);
                _logger?.Information("Loaded achievement image from assets");
            }
            catch (Exception packEx)
            {
                _logger?.Warning(packEx, "Achievement image not found: {Name}", imageName);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to load achievement image: {Name}", imageName);
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
            _logger?.Error(ex, "Error during fade out, closing directly");
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
        _logger?.Information("AchievementPopup closed");
    }
}
