using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Localization;

using IModService = ConditioningControlPanel.IModService;
using Animation = global::Avalonia.Animation.Animation;
using KeyFrame = global::Avalonia.Animation.KeyFrame;
using FillMode = global::Avalonia.Animation.FillMode;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of PinkRushPopup: a toast shown when Pink Rush activates.
/// </summary>
public partial class PinkRushPopup : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private readonly DispatcherTimer _countdownTimer;

    public PinkRushPopup()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
// Apply mod overrides to text if a mod service is available.
        try
        {
            var mods = App.Services.GetService<IModService>();
            if (mods != null)
            {
                TxtPinkRushTitle.Text = "⚡ " + mods.GetPinkRushName();
                TxtPinkRushSubtitle.Text = mods.GetPinkRushDescription();
            }
        }
        catch { }

        LoadSkillImage();
        PositionWindow();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += CountdownTimer_Tick;
        _countdownTimer.Start();

        UpdateCountdown();

        Opacity = 0;
        Loaded += async (s, e) =>
        {
            await RunFadeAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        };
    }

    private void LoadSkillImage()
    {
        try
        {
            var env = App.Services?.GetService<IAppEnvironment>();
            var assetLoader = App.Services?.GetService<IAssetLoader>();
            var filePath = env != null
                ? Path.Combine(env.BaseDirectory, "Resources", "skills", "pink_rush.png")
                : Path.Combine(AppContext.BaseDirectory, "Resources", "skills", "pink_rush.png");

            if (File.Exists(filePath))
            {
                ImgSkill.Source = new Bitmap(filePath);
                return;
            }

            var avares = new Uri("avares://CCP.Avalonia/Assets/skills/pink_rush.png");
            if (assetLoader?.Exists(avares) == true)
            {
                using var stream = assetLoader.Open(avares);
                ImgSkill.Source = new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "PinkRushPopup: failed to load skill image");
        }
    }

    private void PositionWindow()
    {
        try
        {
            var provider = App.Services?.GetService<IScreenProvider>();
            var screen = provider?.GetPrimaryScreen();
            if (screen == null) return;

            var workArea = screen.WorkingArea;
            Position = new PixelPoint(
                (int)(workArea.X + workArea.Width - (Width * screen.Scaling) - 20),
                (int)(workArea.Y + workArea.Height - (Height * screen.Scaling) - 20));
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to position Pink Rush popup");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        var settings = App.Services?.GetService<ISettingsService>()?.Current;
        var endTime = settings?.PinkRushEndTime;
        if (endTime == null)
        {
            _countdownTimer.Stop();
            FadeOutAndClose();
            return;
        }

        var remaining = endTime.Value - DateTime.Now;
        if (remaining.TotalSeconds <= 0)
        {
            _countdownTimer.Stop();
            FadeOutAndClose();
            return;
        }

        TxtCountdown.Text = string.Format(Loc.Get("window_pink_rush_seconds_remaining_fmt"), (int)remaining.TotalSeconds);
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
        catch
        {
            Close();
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        _countdownTimer.Stop();
        FadeOutAndClose();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        FadeOutAndClose();
    }

    protected override void OnClosed(EventArgs e)
    {
        _countdownTimer.Stop();
        base.OnClosed(e);
    }
}
