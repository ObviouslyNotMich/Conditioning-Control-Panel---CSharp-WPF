using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Localization;

using Animation = global::Avalonia.Animation.Animation;
using KeyFrame = global::Avalonia.Animation.KeyFrame;
using FillMode = global::Avalonia.Animation.FillMode;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of QuestCompletePopup: a small toast shown when a quest is completed.
/// </summary>
public partial class QuestCompletePopup : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private readonly DispatcherTimer _autoCloseTimer;

    public QuestCompletePopup(string questName, int xpAwarded)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        TxtQuestName.Text = questName;
        TxtXPAwarded.Text = $"+{xpAwarded} {Loc.Get("label_xp")}";

        PositionWindow();

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            FadeOutAndClose();
        };
        _autoCloseTimer.Start();

        Opacity = 0;
        Loaded += async (s, e) =>
        {
            await RunFadeAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        };
    }

    /// <summary>
    /// Required parameterless constructor for Avalonia designer/build.
    /// </summary>
    public QuestCompletePopup()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_autoCloseTimer = new DispatcherTimer();
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
            _logger?.Error(ex, "Failed to position quest complete popup");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
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
        catch
        {
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
    }
}
