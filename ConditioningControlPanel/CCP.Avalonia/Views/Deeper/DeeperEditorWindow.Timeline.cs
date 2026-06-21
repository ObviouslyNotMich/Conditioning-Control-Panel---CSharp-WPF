using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Visual timeline tab for the Avalonia Deeper editor.
/// Renders three lanes (Regions, Effects, Haptics), a time ruler, playhead,
/// and the items in each lane. Supports click-to-select, click-to-seek,
/// and horizontal zoom via buttons / Ctrl+wheel.
/// </summary>
public partial class DeeperEditorWindow
{
    // Timeline state
    private double _totalSeconds;
    private double _currentSeconds;
    private double _zoomFactor = 1.0;
    private const double MinZoom = 1.0;
    private const double MaxZoom = 16.0;
    private const double RulerHeight = 18.0;
    private const double MinBandVisualWidthPx = 8.0;

    private readonly List<Control> _timelineVisuals = new();
    private readonly List<Control> _rulerVisuals = new();
    private bool _isScrubbingPlayhead;

    private static readonly Color RegionDefaultColor = Color.Parse("#7B5CFF");
    private static readonly Color HapticColor = Color.Parse("#7B5CFF");

    private static readonly Dictionary<string, Color> EffectColors = new()
    {
        [EffectTypes.Haptic] = Color.Parse("#7B5CFF"),
        [EffectTypes.Flash] = Color.Parse("#FFC85C"),
        [EffectTypes.Bubble] = Color.Parse("#5CC8FF"),
        [EffectTypes.Subliminal] = Color.Parse("#FF69B4"),
        [EffectTypes.Overlay] = Color.Parse("#5CFFB7"),
    };

    private void InitializeTimeline()
    {
        if (TimelineCanvas != null)
            TimelineCanvas.SizeChanged += (_, _) => RebuildTimeline();
        RefreshTimelineTransport();
    }

    private void RecomputeTotalDuration()
    {
        double max = 0.0;
        foreach (var r in _enhancement.Regions)
            if (r.End > max) max = r.End;
        foreach (var track in _enhancement.HapticTracks)
            foreach (var ev in track.Events)
                if (ev.Start + ev.Duration > max) max = ev.Start + ev.Duration;
        foreach (var item in _enhancement.TimelineItems)
        {
            var end = item.Start + Math.Max(0.0, item.Duration);
            if (end > max) max = end;
        }
        foreach (var rule in _enhancement.Rules)
        {
            if (rule.Trigger is TimeReachedTrigger t && t.Time > max) max = t.Time;
        }
        _totalSeconds = Math.Max(10.0, max + 5.0);
    }

    private void RebuildTimeline()
    {
        if (TimelineCanvas == null) return;
        RecomputeTotalDuration();
        ClearTimelineVisuals();
        RefreshTimelineTransport();

        var canvasWidth = TimelineCanvas.Bounds.Width;
        var canvasHeight = TimelineCanvas.Bounds.Height;
        if (canvasWidth <= 0 || canvasHeight <= 0 || _totalSeconds <= 0) return;

        canvasWidth *= _zoomFactor;
        TimelineCanvas.Width = canvasWidth;

        DrawRuler(canvasWidth);
        DrawLaneDividers(canvasWidth, canvasHeight);
        DrawRegions(canvasWidth, canvasHeight);
        DrawEffects(canvasWidth, canvasHeight);
        DrawHaptics(canvasWidth, canvasHeight);
        DrawRulePins(canvasWidth, canvasHeight);
        UpdatePlayheadPosition();
        EnsurePlayheadOnTop();
        RefreshLaneCounts();
    }

    private void ClearTimelineVisuals()
    {
        foreach (var v in _timelineVisuals)
        {
            try { TimelineCanvas!.Children.Remove(v); } catch { }
        }
        _timelineVisuals.Clear();
        foreach (var v in _rulerVisuals)
        {
            try { TimelineCanvas!.Children.Remove(v); } catch { }
        }
        _rulerVisuals.Clear();
    }

    private void DrawRuler(double canvasWidth)
    {
        var stripBrush = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
        var strip = new Rectangle
        {
            Width = canvasWidth,
            Height = RulerHeight,
            Fill = stripBrush,
        };
        Canvas.SetLeft(strip, 0);
        Canvas.SetTop(strip, 0);
        strip.ZIndex = 50;
        TimelineCanvas!.Children.Add(strip);
        _rulerVisuals.Add(strip);

        var pxPerSec = canvasWidth / _totalSeconds;
        if (pxPerSec <= 0) return;

        var majorSec = PickNiceTickInterval(70.0 / pxPerSec);
        var minorSec = majorSec / 5.0;

        var majorBrush = new SolidColorBrush(Color.FromArgb(200, 230, 230, 230));
        var minorBrush = new SolidColorBrush(Color.FromArgb(110, 230, 230, 230));
        var labelBrush = new SolidColorBrush(Color.FromArgb(255, 235, 235, 235));

        if (minorSec > 0)
        {
            int n = (int)Math.Ceiling(_totalSeconds / minorSec);
            for (int i = 0; i <= n; i++)
            {
                double t = i * minorSec;
                if (t > _totalSeconds) break;
                if (i % 5 == 0) continue;
                var x = (t / _totalSeconds) * canvasWidth;
                var line = new Line
                {
                    StartPoint = new Point(x, RulerHeight - 4),
                    EndPoint = new Point(x, RulerHeight),
                    Stroke = minorBrush,
                    StrokeThickness = 1,
                };
                line.ZIndex = 51;
                TimelineCanvas.Children.Add(line);
                _rulerVisuals.Add(line);
            }
        }

        int m = (int)Math.Floor(_totalSeconds / majorSec);
        for (int i = 0; i <= m; i++)
        {
            double t = i * majorSec;
            var x = (t / _totalSeconds) * canvasWidth;
            var tick = new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, RulerHeight),
                Stroke = majorBrush,
                StrokeThickness = 1,
            };
            tick.ZIndex = 52;
            TimelineCanvas.Children.Add(tick);
            _rulerVisuals.Add(tick);

            if (i == 0) continue;
            var label = new TextBlock
            {
                Text = FormatRulerTime(t, majorSec),
                Foreground = labelBrush,
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
            };
            var labelW = label.DesiredSize.Width > 0 ? label.DesiredSize.Width : 24;
            var labelX = x + 2;
            if (labelX + labelW > canvasWidth) labelX = Math.Max(0, x - labelW - 2);
            Canvas.SetLeft(label, labelX);
            Canvas.SetTop(label, 1);
            label.ZIndex = 53;
            TimelineCanvas.Children.Add(label);
            _rulerVisuals.Add(label);
        }
    }

    private static double PickNiceTickInterval(double minSecondsPerTick)
    {
        double[] intervals = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600, 7200, 18000, 36000 };
        foreach (var n in intervals)
            if (n >= minSecondsPerTick) return n;
        return intervals[^1];
    }

    private static string FormatRulerTime(double seconds, double intervalSec)
    {
        if (seconds < 0) seconds = 0;
        if (intervalSec < 1.0)
            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
    }

    private void DrawLaneDividers(double canvasWidth, double canvasHeight)
    {
        var laneH = canvasHeight / 3.0;
        var dividerBrush = new SolidColorBrush(Color.FromArgb(40, 230, 230, 230));
        for (int i = 1; i < 3; i++)
        {
            var y = i * laneH;
            var line = new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(canvasWidth, y),
                Stroke = dividerBrush,
                StrokeThickness = 1,
            };
            line.ZIndex = 1;
            TimelineCanvas!.Children.Add(line);
            _timelineVisuals.Add(line);
        }
    }

    private static (double top, double height) LaneBand(TimelineLane lane, double canvasHeight)
    {
        if (canvasHeight <= 0) return (0, 0);
        var laneH = canvasHeight / 3.0;
        var top = lane switch
        {
            TimelineLane.Regions => 0,
            TimelineLane.Effects => laneH,
            TimelineLane.Haptics => 2 * laneH,
            _ => 0,
        };
        return (top, laneH);
    }

    private enum TimelineLane { Regions, Effects, Haptics }

    private void DrawRegions(double canvasWidth, double canvasHeight)
    {
        var (top, height) = LaneBand(TimelineLane.Regions, canvasHeight);
        var inset = 3.0;
        var rectHeight = Math.Max(4, height - 2 * inset);
        var y = top + inset;

        foreach (var region in _enhancement.Regions)
        {
            var startX = Math.Max(0, (region.Start / _totalSeconds) * canvasWidth);
            var endX = Math.Min(canvasWidth, (region.End / _totalSeconds) * canvasWidth);
            var width = Math.Max(MinBandVisualWidthPx, endX - startX);
            var color = ParseHexColor(region.Color) ?? RegionDefaultColor;
            var fill = new Color(160, color.R, color.G, color.B);
            var isSelected = LstRegions.SelectedItem == region;

            var rect = new Rectangle
            {
                Width = width,
                Height = rectHeight,
                Fill = new SolidColorBrush(fill),
                Stroke = new SolidColorBrush(isSelected ? Colors.White : color),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = new Cursor(StandardCursorType.SizeAll),
                Tag = region,
            };
            ToolTip.SetTip(rect, $"{region.Label ?? region.Id} ({region.Start:0.##}s–{region.End:0.##}s)");
            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, y);
            rect.ZIndex = 10;
            rect.PointerPressed += RegionRect_PointerPressed;
            TimelineCanvas!.Children.Add(rect);
            _timelineVisuals.Add(rect);
        }
    }

    private void DrawEffects(double canvasWidth, double canvasHeight)
    {
        var (top, height) = LaneBand(TimelineLane.Effects, canvasHeight);
        var laneInset = 3.0;
        var segmentHeight = Math.Min(18, Math.Max(4, height - 2 * laneInset));
        var y = top + (height - segmentHeight) / 2.0;

        foreach (var item in _enhancement.TimelineItems)
        {
            if (item.Kind != TimelineItemKind.Effect) continue;
            if (item.EffectType == EffectTypes.Haptic) continue;

            var color = EffectColors.TryGetValue(item.EffectType ?? "", out var c) ? c : Colors.White;
            var fill = new Color(140, color.R, color.G, color.B);
            var isSelected = LstRules.SelectedItem is EnhancementRule r && RuleRefersToEffect(r, item);

            if (IsOneShotEffect(item.EffectType))
            {
                var x = (item.Start / _totalSeconds) * canvasWidth - 6;
                var dot = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(color),
                    Stroke = new SolidColorBrush(isSelected ? Colors.White : Colors.Transparent),
                    StrokeThickness = isSelected ? 2 : 0,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = item,
                };
                ToolTip.SetTip(dot, $"{item.EffectType} @ {item.Start:0.##}s");
                Canvas.SetLeft(dot, x);
                Canvas.SetTop(dot, top + height / 2.0 - 6);
                dot.ZIndex = 10;
                dot.PointerPressed += EffectDot_PointerPressed;
                TimelineCanvas!.Children.Add(dot);
                _timelineVisuals.Add(dot);
            }
            else
            {
                var startX = Math.Max(0, (item.Start / _totalSeconds) * canvasWidth);
                var endX = Math.Min(canvasWidth, ((item.Start + Math.Max(0, item.Duration)) / _totalSeconds) * canvasWidth);
                var width = Math.Max(MinBandVisualWidthPx, endX - startX);
                var rect = new Rectangle
                {
                    Width = width,
                    Height = segmentHeight,
                    Fill = new SolidColorBrush(fill),
                    Stroke = new SolidColorBrush(isSelected ? Colors.White : color),
                    StrokeThickness = isSelected ? 2.0 : 1.0,
                    Cursor = new Cursor(StandardCursorType.SizeAll),
                    Tag = item,
                };
                ToolTip.SetTip(rect, $"{item.EffectType} @ {item.Start:0.##}s · {item.Duration:0.##}s");
                Canvas.SetLeft(rect, startX);
                Canvas.SetTop(rect, y);
                rect.ZIndex = 10;
                rect.PointerPressed += EffectRect_PointerPressed;
                TimelineCanvas!.Children.Add(rect);
                _timelineVisuals.Add(rect);
            }
        }
    }

    private static bool IsOneShotEffect(string? effectType)
        => effectType == EffectTypes.Flash || effectType == EffectTypes.Subliminal;

    private void DrawHaptics(double canvasWidth, double canvasHeight)
    {
        var (top, height) = LaneBand(TimelineLane.Haptics, canvasHeight);
        var inset = 3.0;
        var rectHeight = Math.Max(4, height - 2 * inset);
        var y = top + inset;

        foreach (var track in _enhancement.HapticTracks)
        {
            foreach (var ev in track.Events)
            {
                var startX = Math.Max(0, (ev.Start / _totalSeconds) * canvasWidth);
                var endX = Math.Min(canvasWidth, ((ev.Start + ev.Duration) / _totalSeconds) * canvasWidth);
                var width = Math.Max(MinBandVisualWidthPx, endX - startX);
                var fill = new Color(160, HapticColor.R, HapticColor.G, HapticColor.B);
                var isSelected = LstHaptics.SelectedItem == ev;

                var rect = new Rectangle
                {
                    Width = width,
                    Height = rectHeight,
                    Fill = new SolidColorBrush(fill),
                    Stroke = new SolidColorBrush(isSelected ? Colors.White : HapticColor),
                    StrokeThickness = isSelected ? 2.0 : 1.0,
                    Cursor = new Cursor(StandardCursorType.SizeAll),
                    Tag = ev,
                };
                ToolTip.SetTip(rect, $"{ev.PatternName ?? "Haptic"} @ {ev.Start:0.##}s · {ev.Duration:0.##}s");
                Canvas.SetLeft(rect, startX);
                Canvas.SetTop(rect, y);
                rect.ZIndex = 10;
                rect.PointerPressed += HapticRect_PointerPressed;
                TimelineCanvas!.Children.Add(rect);
                _timelineVisuals.Add(rect);
            }
        }
    }

    private void DrawRulePins(double canvasWidth, double canvasHeight)
    {
        var pinColor = Color.Parse("#FF8C00");
        var brush = new SolidColorBrush(pinColor);
        int idx = 0;
        foreach (var rule in _enhancement.Rules)
        {
            idx++;
            if (rule.Trigger is not TimeReachedTrigger tr) continue;
            var x = (Math.Max(0, tr.Time) / _totalSeconds) * canvasWidth;
            var isSelected = LstRules.SelectedItem == rule;

            var line = new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, canvasHeight),
                Stroke = brush,
                StrokeThickness = isSelected ? 2.5 : 1.5,
                StrokeDashArray = new AvaloniaList<double> { 4, 3 },
            };
            line.ZIndex = 9;
            TimelineCanvas!.Children.Add(line);
            _timelineVisuals.Add(line);

            var flag = new Polygon
            {
                Points = new Points { new(x, 2), new(x + 12, 6), new(x, 10) },
                Fill = brush,
                Stroke = new SolidColorBrush(isSelected ? Colors.White : Colors.Transparent),
                StrokeThickness = isSelected ? 1.5 : 0,
            };
            flag.ZIndex = 10;
            TimelineCanvas.Children.Add(flag);
            _timelineVisuals.Add(flag);

            var hit = new Rectangle
            {
                Width = 14,
                Height = canvasHeight,
                Fill = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = rule,
            };
            ToolTip.SetTip(hit, $"Rule #{idx} · time {tr.Time:0.##}s");
            Canvas.SetLeft(hit, x - 7);
            Canvas.SetTop(hit, 0);
            hit.ZIndex = 11;
            hit.PointerPressed += RulePin_PointerPressed;
            TimelineCanvas.Children.Add(hit);
            _timelineVisuals.Add(hit);
        }
    }

    private void UpdatePlayheadPosition()
    {
        if (TimelineCanvas == null || PlayheadLine == null) return;
        var canvasWidth = TimelineCanvas.Bounds.Width * _zoomFactor;
        var x = _totalSeconds > 0 ? (_currentSeconds / _totalSeconds) * canvasWidth : 0;
        var y2 = TimelineCanvas.Bounds.Height;
        PlayheadLine.StartPoint = new Point(x, 0);
        PlayheadLine.EndPoint = new Point(x, y2);
    }

    private void EnsurePlayheadOnTop()
    {
        if (PlayheadLine != null) PlayheadLine.ZIndex = 100;
    }

    private void RefreshTimelineTransport()
    {
        if (TxtCurrentTime != null) TxtCurrentTime.Text = FormatTimeShort(_currentSeconds);
        if (TxtTotalTime != null) TxtTotalTime.Text = FormatTimeShort(_totalSeconds);
        if (TxtZoomLevel != null) TxtZoomLevel.Text = $"{(int)(_zoomFactor * 100)}%";
    }

    private void RefreshLaneCounts()
    {
        try
        {
            int regions = _enhancement.Regions.Count;
            int haptics = _enhancement.HapticTracks.Sum(t => t.Events.Count);
            int effects = _enhancement.TimelineItems.Count(ti => ti.Kind == TimelineItemKind.Effect && ti.EffectType != EffectTypes.Haptic);

            if (TxtRegionsLaneCount != null)
                TxtRegionsLaneCount.Text = regions == 0 ? "" : regions.ToString(CultureInfo.InvariantCulture);
            if (TxtEffectsLaneCount != null)
                TxtEffectsLaneCount.Text = effects == 0 ? "" : effects.ToString(CultureInfo.InvariantCulture);
            if (TxtHapticsLaneCount != null)
                TxtHapticsLaneCount.Text = haptics == 0 ? "" : haptics.ToString(CultureInfo.InvariantCulture);
        }
        catch { }
    }

    private static string FormatTimeShort(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        int total = (int)Math.Round(seconds);
        int m = total / 60;
        int s = total % 60;
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}", m, s);
    }

    private double MouseToSeconds(Point point)
    {
        var canvasWidth = TimelineCanvas!.Bounds.Width * _zoomFactor;
        if (canvasWidth <= 0) return 0;
        var frac = Math.Clamp(point.X / canvasWidth, 0, 1);
        return frac * _totalSeconds;
    }

    private void SeekToSeconds(double seconds)
    {
        _currentSeconds = Math.Clamp(seconds, 0, _totalSeconds);
        UpdatePlayheadPosition();
        RefreshTimelineTransport();
    }

    // ========================================================================
    // Pointer handlers
    // ========================================================================

    private void TimelineCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TimelineCanvas == null) return;
        var point = e.GetCurrentPoint(TimelineCanvas);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isScrubbingPlayhead = true;
            SeekToSeconds(MouseToSeconds(point.Position));
        }
    }

    private void TimelineCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (TimelineCanvas == null) return;
        if (_isScrubbingPlayhead)
        {
            var point = e.GetCurrentPoint(TimelineCanvas);
            SeekToSeconds(MouseToSeconds(point.Position));
        }
    }

    private void TimelineCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isScrubbingPlayhead = false;
    }

    private void TimelineCanvas_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            var delta = e.Delta.Y > 0 ? 1.25 : 0.8;
            _zoomFactor = Math.Clamp(_zoomFactor * delta, MinZoom, MaxZoom);
            RebuildTimeline();
        }
    }

    private void BtnZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        _zoomFactor = Math.Clamp(_zoomFactor * 1.25, MinZoom, MaxZoom);
        RebuildTimeline();
    }

    private void BtnZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        _zoomFactor = Math.Clamp(_zoomFactor * 0.8, MinZoom, MaxZoom);
        RebuildTimeline();
    }

    // ========================================================================
    // Item selection from timeline
    // ========================================================================

    private void RegionRect_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle rect || rect.Tag is not Region region) return;
        e.Handled = true;
        LstRegions.SelectedItem = region;
        EditorTabControl.SelectedIndex = 2; // Regions tab
    }

    private void EffectRect_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle rect || rect.Tag is not TimelineItem item) return;
        e.Handled = true;
        SelectRuleForEffect(item);
        EditorTabControl.SelectedIndex = 3; // Rules tab
    }

    private void EffectDot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Ellipse dot || dot.Tag is not TimelineItem item) return;
        e.Handled = true;
        SelectRuleForEffect(item);
        EditorTabControl.SelectedIndex = 3; // Rules tab
    }

    private void HapticRect_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle rect || rect.Tag is not HapticEvent ev) return;
        e.Handled = true;
        LstHaptics.SelectedItem = ev;
        EditorTabControl.SelectedIndex = 4; // Haptics tab
    }

    private void RulePin_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle rect || rect.Tag is not EnhancementRule rule) return;
        e.Handled = true;
        LstRules.SelectedItem = rule;
        EditorTabControl.SelectedIndex = 3; // Rules tab
    }

    private void SelectRuleForEffect(TimelineItem item)
    {
        foreach (var rule in _enhancement.Rules)
        {
            if (rule.Action is TriggerEffectAction a && a.EffectType == item.EffectType)
            {
                LstRules.SelectedItem = rule;
                return;
            }
        }
    }

    private static bool RuleRefersToEffect(EnhancementRule rule, TimelineItem item)
    {
        return rule.Action is TriggerEffectAction a && a.EffectType == item.EffectType;
    }

    private static Color? ParseHexColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return Color.Parse(hex); }
        catch { return null; }
    }
}
