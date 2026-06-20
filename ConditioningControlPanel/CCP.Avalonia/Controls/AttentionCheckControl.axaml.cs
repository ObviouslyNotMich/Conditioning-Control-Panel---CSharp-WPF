using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Animation = global::Avalonia.Animation.Animation;
using IterationCount = global::Avalonia.Animation.IterationCount;
using KeyFrame = global::Avalonia.Animation.KeyFrame;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>
/// Reusable visual for the Attention-Check mechanic: hot-pink progress
/// ring around a glowing dot. The control is intrinsically 84x84 DIPs;
/// host it in a window at the desired screen position.
///
/// Public surface:
///   SetProgress(0..1)  — fills the foreground ring clockwise.
///   StartPulse() / StopPulse() — gentle scale-pulse animation, useful
///                                 to signal "look here" on first appear.
/// </summary>
public partial class AttentionCheckControl : UserControl
{
    private CancellationTokenSource? _pulseCts;
    private readonly ScaleTransform _dotRingScale;

    public AttentionCheckControl()
    {
        InitializeComponent();
        _dotRingScale = new ScaleTransform(1.0, 1.0);
        DotRingFg.RenderTransform = new TransformGroup
        {
            Children =
            {
                new RotateTransform(-90.0),
                _dotRingScale
            }
        };
    }

    /// <summary>
    /// Sets the foreground-ring fill amount. progress is clamped to
    /// [0, 1]; 0 = empty, 1 = full ring.
    /// </summary>
    public void SetProgress(double progress)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        double radius = (DotRingFg.Width - DotRingFg.StrokeThickness) / 2.0;
        double perimeter = 2.0 * Math.PI * radius;
        double units = perimeter / DotRingFg.StrokeThickness;
        double visible = progress * units;
        double gap = Math.Max(0.001, units - visible);
        DotRingFg.StrokeDashArray = new AvaloniaList<double>(visible, gap);
    }

    public void StartPulse()
    {
        StopPulse();
        _pulseCts = new CancellationTokenSource();

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(840),
            IterationCount = IterationCount.Infinite,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.0),
                        new Setter(ScaleTransform.ScaleYProperty, 1.0)
                    },
                    KeyTime = TimeSpan.Zero
                },
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.18),
                        new Setter(ScaleTransform.ScaleYProperty, 1.18)
                    },
                    KeyTime = TimeSpan.FromMilliseconds(420)
                },
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.0),
                        new Setter(ScaleTransform.ScaleYProperty, 1.0)
                    },
                    KeyTime = TimeSpan.FromMilliseconds(840)
                }
            }
        };

        _ = animation.RunAsync(_dotRingScale, _pulseCts.Token);
    }

    public void StopPulse()
    {
        _pulseCts?.Cancel();
        _pulseCts?.Dispose();
        _pulseCts = null;
        _dotRingScale.ScaleX = 1.0;
        _dotRingScale.ScaleY = 1.0;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        StopPulse();
        base.OnUnloaded(e);
    }
}
