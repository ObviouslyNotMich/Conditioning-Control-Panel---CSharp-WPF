using System;
using global::Avalonia;
using global::Avalonia.Animation;
using global::Avalonia.Media;
using global::Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>DispatcherTimer-based animation helpers for overlay code-behind.
/// Mirrors the WPF animation contracts (opacity fades, scale pulses, double tweens)
/// without requiring Avalonia's animation system on keep-alive overlay windows.
/// TODO: replace with Avalonia Animation classes once the overlay lifetime model is stable.</summary>
internal sealed class OpacityFade : IDisposable
{
    private readonly global::Avalonia.Controls.Control _target;
    private readonly DispatcherTimer _timer = new();
    private readonly double _from;
    private readonly double _to;
    private readonly double _durationMs;
    private readonly double _startMs;
    private readonly Action? _onComplete;
    private bool _done;

    public OpacityFade(global::Avalonia.Controls.Control target, double from, double to,
                       double durationMs, Action? onComplete = null)
    {
        _target = target;
        _from = from;
        _to = to;
        _durationMs = Math.Max(1, durationMs);
        _startMs = Environment.TickCount64;
        _onComplete = onComplete;
        _target.Opacity = from;
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += Tick;
        _timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_done) return;
        double elapsed = Environment.TickCount64 - _startMs;
        double t = Math.Min(1, elapsed / _durationMs);
        _target.Opacity = _from + (_to - _from) * t;
        if (t >= 1)
        {
            _done = true;
            _timer.Stop();
            _onComplete?.Invoke();
        }
    }

    public void Cancel()
    {
        _done = true;
        _timer.Stop();
    }

    public void Dispose()
    {
        Cancel();
        _timer.Tick -= Tick;
    }
}

/// <summary>Forever scale pulse (e.g. cursor halos, ready toy buttons).
/// Mimics WPF DoubleAnimation with AutoReverse + RepeatBehavior.Forever.</summary>
internal sealed class ScalePulse : IDisposable
{
    private readonly DispatcherTimer _timer = new();
    private readonly ScaleTransform _target;
    private readonly double _min;
    private readonly double _max;
    private readonly double _halfPeriodMs;
    private readonly double _startMs;
    private bool _done;

    public ScalePulse(ScaleTransform target, double min, double max, double periodMs)
    {
        _target = target;
        _min = min;
        _max = max;
        _halfPeriodMs = Math.Max(1, periodMs / 2.0);
        _startMs = Environment.TickCount64;
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += Tick;
        _timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_done) return;
        double elapsed = Environment.TickCount64 - _startMs;
        double phase = (elapsed % (_halfPeriodMs * 2)) / _halfPeriodMs; // 0..2
        double t = phase <= 1 ? phase : 2 - phase; // 0..1..0 triangle
        // Sine ease in-out approximation for the WPF SineEase feel.
        t = (1 - Math.Cos(t * Math.PI)) / 2.0;
        double s = _min + (_max - _min) * t;
        _target.ScaleX = s;
        _target.ScaleY = s;
    }

    public void Cancel()
    {
        _done = true;
        _timer.Stop();
    }

    public void Dispose()
    {
        Cancel();
        _timer.Tick -= Tick;
    }
}

/// <summary>One-shot double animation on an Avalonia animatable property.
/// Used for pop/bounce transforms where a full storyboard is overkill.</summary>
internal static class AvaloniaChaosAnim
{
    public static void AnimateDouble(Animatable target, AvaloniaProperty property,
                                     double from, double to, double durationMs,
                                     EasingMode easing = EasingMode.EaseOut)
    {
        double startMs = Environment.TickCount64;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double elapsed = Environment.TickCount64 - startMs;
            double t = Math.Min(1, elapsed / Math.Max(1, durationMs));
            double eased = easing switch
            {
                EasingMode.EaseIn => t * t,
                EasingMode.EaseInOut => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
                _ => 1 - Math.Pow(1 - t, 3), // EaseOut cubic default
            };
            double value = from + (to - from) * eased;
            target.SetValue(property, value);
            if (t >= 1) timer.Stop();
        };
        target.SetValue(property, from);
        timer.Start();
    }

    public enum EasingMode { EaseOut, EaseIn, EaseInOut }
}
