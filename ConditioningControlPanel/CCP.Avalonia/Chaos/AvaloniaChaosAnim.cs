using System;
using global::Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>Simple DispatcherTimer-based opacity animation helper for overlay code-behind.
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
