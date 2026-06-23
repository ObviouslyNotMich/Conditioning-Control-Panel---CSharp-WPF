using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Point = Avalonia.Point;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Simple curve editor for custom haptic patterns.
/// Renders a polyline through fixed keyframes and lets the user drag each
/// handle vertically to change intensity. The underlying CustomPattern is a
/// list of [time, intensity] pairs where time is normalized 0..1.
/// </summary>
public partial class DeeperEditorWindow
{
    private const int CurveKeyframeCount = 5;
    private global::Avalonia.Controls.Shapes.Path? _curvePath;
    private readonly List<Ellipse> _curveHandles = new();
    private int _draggingCurveIndex = -1;

    private void CurveEditorCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        RebuildCurveEditor();
    }

    private void RebuildCurveEditor()
    {
        if (CurveEditorCanvas == null) return;
        CurveEditorCanvas.Children.Clear();
        _curvePath = null;
        _curveHandles.Clear();

        if (LstHaptics.SelectedItem is not HapticEvent ev) return;
        if (ev.CustomPattern == null || ev.CustomPattern.Count == 0) return;

        var w = CurveEditorCanvas.Bounds.Width;
        var h = CurveEditorCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        // Background grid
        var gridBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        for (int i = 1; i < 4; i++)
        {
            var y = h * i / 4.0;
            var line = new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(w, y),
                Stroke = gridBrush,
                StrokeThickness = 0.5,
            };
            CurveEditorCanvas.Children.Add(line);
        }

        // Polyline through keyframes
        var pts = ev.CustomPattern;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(KeyframeToCanvas(pts[0], w, h), false);
            for (int i = 1; i < pts.Count; i++)
                ctx.LineTo(KeyframeToCanvas(pts[i], w, h));
        }

        var accent = this.FindResource("DeeperAccentBrush") as IBrush ?? new SolidColorBrush(Color.Parse("#FF7B5CFF"));
        _curvePath = new global::Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Stroke = accent,
            StrokeThickness = 1.6,
        };
        CurveEditorCanvas.Children.Add(_curvePath);

        // Draggable handles
        for (int i = 0; i < pts.Count; i++)
        {
            var pt = KeyframeToCanvas(pts[i], w, h);
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = accent,
                Stroke = Brushes.White,
                StrokeThickness = 1.2,
                Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Tag = i,
            };
            Canvas.SetLeft(dot, pt.X - 5);
            Canvas.SetTop(dot, pt.Y - 5);
            dot.PointerPressed += CurveHandle_PointerPressed;
            dot.PointerMoved += CurveHandle_PointerMoved;
            dot.PointerReleased += CurveHandle_PointerReleased;
            CurveEditorCanvas.Children.Add(dot);
            _curveHandles.Add(dot);
        }
    }

    private static Point KeyframeToCanvas(double[] kf, double w, double h)
    {
        var t = Math.Clamp(kf.Length > 0 ? kf[0] : 0, 0, 1);
        var v = Math.Clamp(kf.Length > 1 ? kf[1] : 0, 0, 1);
        return new Point(t * w, h - (v * h));
    }

    private void CurveHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Ellipse el && el.Tag is int idx)
        {
            _draggingCurveIndex = idx;
            e.Handled = true;
        }
    }

    private void CurveHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingCurveIndex < 0 || LstHaptics.SelectedItem is not HapticEvent ev) return;
        if (ev.CustomPattern == null || _draggingCurveIndex >= ev.CustomPattern.Count) return;

        var pt = e.GetPosition(CurveEditorCanvas);
        var h = CurveEditorCanvas!.Bounds.Height;
        if (h <= 0) return;

        var v = Math.Clamp(1.0 - (pt.Y / h), 0.0, 1.0);
        ev.CustomPattern[_draggingCurveIndex][1] = v;
        MarkDirty();
        RebuildCurveEditor();
        SyncHapticCustomTextFromPattern();
    }

    private void CurveHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingCurveIndex = -1;
    }

    private void CurveEditorCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Canvas-level interactions (e.g. adding keyframes) can be added later.
    }

    private void CurveEditorCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        // Reserved for canvas-level drag gestures.
    }

    private void CurveEditorCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingCurveIndex = -1;
    }

    private void SyncHapticCustomTextFromPattern()
    {
        if (LstHaptics.SelectedItem is not HapticEvent ev || ev.CustomPattern == null) return;
        var json = "[" + string.Join(",", ev.CustomPattern.Select(k => $"[{k[0]:F2},{k[1]:F2}]")) + "]";
        if (TxtHapticCustom != null) TxtHapticCustom.Text = json;
    }

    private void EnsureCurveSeed(HapticEvent ev)
    {
        if (ev.CustomPattern == null || ev.CustomPattern.Count < 2)
            ev.CustomPattern = StockHapticPatterns.SeedCustomFrom(ev.PatternName);
    }
}
