using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Core;

using Animation = global::Avalonia.Animation.Animation;
using KeyFrame = global::Avalonia.Animation.KeyFrame;
using Easing = global::Avalonia.Animation.Easings;
using FillMode = global::Avalonia.Animation.FillMode;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the startup splash screen.
/// </summary>
public partial class SplashScreen : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    public SplashScreen()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
TxtVersion.Text = $"v{CCPCore.Version}";
    }

    /// <summary>
    /// Update the progress bar and status text.
    /// </summary>
    public void SetProgress(double progress, string status)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetProgress(progress, status));
            return;
        }

        TxtStatus.Text = status;

        var target = Math.Clamp(progress, 0.0, 1.0);
        var transform = new ScaleTransform(0, 1);
        ProgressFill.RenderTransform = transform;
        ProgressFill.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            FillMode = FillMode.Forward,
            Easing = new Easing.QuadraticEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Setters = { new Setter(ScaleTransform.ScaleXProperty, target) },
                    KeyTime = TimeSpan.FromMilliseconds(150)
                }
            }
        };

        _ = animation.RunAsync(transform);
    }

    /// <summary>
    /// Close the splash screen with a fade-out animation.
    /// </summary>
    public async void FadeOutAndClose()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(FadeOutAndClose);
            return;
        }

        var animation =
new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Setters = { new Setter(OpacityProperty, 1.0) },
                    KeyTime = TimeSpan.Zero
                },
                new KeyFrame
                {
                    Setters = { new Setter(OpacityProperty, 0.0) },
                    KeyTime = TimeSpan.FromMilliseconds(200)
                }
            }
        };

        try
        {
            await animation.RunAsync(this);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "SplashScreen fade-out failed");
        }

        Close();
    }
}
