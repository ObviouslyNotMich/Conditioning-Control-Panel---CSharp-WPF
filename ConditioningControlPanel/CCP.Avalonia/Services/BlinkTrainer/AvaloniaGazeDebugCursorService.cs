using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Point = ConditioningControlPanel.Core.Platform.Point;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BlinkTrainer;
using ConditioningControlPanel.Core.Services.Webcam;

namespace ConditioningControlPanel.Avalonia.Services.BlinkTrainer;

/// <summary>
/// Avalonia implementation of the gaze-debug dot: a small, reference-counted, click-through
/// topmost window that follows the calibrated gaze point.
/// </summary>
public sealed class AvaloniaGazeDebugCursorService : IGazeDebugCursorService, IDisposable
{
    private const double CursorSize = 14.0;

    private static readonly Brush IdleFill =
        new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0x69, 0xB4));

    private static readonly Brush LockedFill =
        new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xD0, 0x80));

    private static readonly Brush Stroke =
        new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    private readonly IWebcamService _webcam;
    private readonly HashSet<string> _requesters = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    private Window? _cursorWindow;
    private Ellipse? _cursorDot;
    private bool _subscribed;
    private bool _faceLost;
    private bool _locked;

    public AvaloniaGazeDebugCursorService(IWebcamService webcam)
    {
        _webcam = webcam;
    }

    public void Show(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_sync)
        {
            _requesters.Add(key);
            UpdateVisibilityLocked();
        }
    }

    public void Hide(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_sync)
        {
            _requesters.Remove(key);
            UpdateVisibilityLocked();
        }
    }

    public void SetLocked(bool locked)
    {
        bool changed;
        lock (_sync)
        {
            changed = _locked != locked;
            _locked = locked;
        }

        if (changed)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_cursorDot != null)
                    _cursorDot.Fill = locked ? LockedFill : IdleFill;
            });
        }
    }

    private void UpdateVisibilityLocked()
    {
        if (_requesters.Count > 0)
            Dispatcher.UIThread.Post(EnsureCursorAndSubscribe);
        else
            Dispatcher.UIThread.Post(DisposeAll);
    }

    private void EnsureCursorAndSubscribe()
    {
        if (_cursorWindow == null)
        {
            try
            {
                _cursorDot = new Ellipse
                {
                    Width = CursorSize,
                    Height = CursorSize,
                    Fill = _locked ? LockedFill : IdleFill,
                    Stroke = Stroke,
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                };

                _cursorWindow = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Focusable = false,
                    IsHitTestVisible = false,
                    CanResize = false,
                    Width = CursorSize,
                    Height = CursorSize,
                    Content = _cursorDot,
                    Position = new PixelPoint(-10000, -10000),
                };

                _cursorWindow.Opened += (_, _) => ChaosWin32Helper.ApplyOverlayExStyles(_cursorWindow, transparent: true);
                _cursorWindow.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GazeDebugCursorService: cursor create failed: {ex.Message}");
                _cursorWindow = null;
                _cursorDot = null;
                return;
            }
        }

        if (!_subscribed)
        {
            _webcam.OnGazeMove += HandleGazeMove;
            _webcam.OnFaceLost += HandleFaceLost;
            _webcam.OnFaceFound += HandleFaceFound;
            _subscribed = true;
        }
    }

    private void DisposeAll()
    {
        if (_subscribed)
        {
            _webcam.OnGazeMove -= HandleGazeMove;
            _webcam.OnFaceLost -= HandleFaceLost;
            _webcam.OnFaceFound -= HandleFaceFound;
            _subscribed = false;
        }

        if (_cursorWindow != null)
        {
            try { _cursorWindow.Close(); } catch { }
            _cursorWindow = null;
            _cursorDot = null;
        }

        _faceLost = false;
    }

    private void HandleGazeMove(Point p)
    {
        if (_cursorWindow == null) return;
        try
        {
            _cursorWindow.Position = new PixelPoint((int)(p.X - CursorSize / 2.0), (int)(p.Y - CursorSize / 2.0));
            if (!_cursorWindow.IsVisible)
                _cursorWindow.Show();
        }
        catch { }
    }

    private void HandleFaceLost()
    {
        _faceLost = true;
        if (_cursorWindow == null) return;
        try { _cursorWindow.Hide(); } catch { }
    }

    private void HandleFaceFound()
    {
        _faceLost = false;
        if (_cursorWindow == null) return;
        try { _cursorWindow.Show(); } catch { }
    }

    public void Dispose()
    {
        lock (_sync) { _requesters.Clear(); }
        Dispatcher.UIThread.Post(DisposeAll);
    }
}
