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
/// drag-create regions, drag-move/resize items, rubber-band multi-select,
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
    private const double EdgeResizePx = 6.0;
    private const double MinResizableRectPx = 24.0;

    private readonly List<Control> _timelineVisuals = new();
    private readonly List<Control> _rulerVisuals = new();
    private readonly HashSet<object> _selectionSet = new();

    private static readonly Color RegionDefaultColor = Color.Parse("#7B5CFF");
    private static readonly Color HapticColor = Color.Parse("#7B5CFF");
    private static readonly string[] RegionPalette = { "#7B5CFF", "#FF69B4", "#5CFFB7", "#FFC85C", "#5CC8FF", "#FF7B5C" };

    private static readonly Dictionary<string, Color> EffectColors = new()
    {
        [EffectTypes.Haptic] = Color.Parse("#7B5CFF"),
        [EffectTypes.Flash] = Color.Parse("#FFC85C"),
        [EffectTypes.Bubble] = Color.Parse("#5CC8FF"),
        [EffectTypes.Subliminal] = Color.Parse("#FF69B4"),
        [EffectTypes.Overlay] = Color.Parse("#5CFFB7"),
    };

    // Drag state
    private enum DragMode
    {
        None, Scrub, CreateRegion, RubberBand,
        DragRegion, ResizeRegionStart, ResizeRegionEnd,
        DragEffect, ResizeEffectStart, ResizeEffectEnd,
        DragHaptic, ResizeHapticStart, ResizeHapticEnd
    }
    private DragMode _dragMode = DragMode.None;

    private Rectangle? _dragCreatePreview;
    private double _dragCreateStartSec;

    private Rectangle? _rubberBandRect;
    private Point _rubberBandStartPoint;

    private Region? _draggedRegion;
    private double _regionDragOffsetSec;
    private double _regionDragOriginalLength;

    private TimelineItem? _draggedEffect;
    private double _effectDragOffsetSec;
    private double _effectDragOriginalDuration;

    private HapticEvent? _draggedHaptic;
    private HapticTrack? _draggedHapticTrack;
    private double _hapticDragOffsetSec;
    private double _hapticDragStartSec;
    private double _hapticDragOriginalDuration;

    private enum EdgeHit { Body, Start, End }
    private enum TimelineLane { Regions, Effects, Haptics }

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

    private static (double top, double height) LaneBandInset(TimelineLane lane, double canvasHeight)
    {
        var (top, height) = LaneBand(lane, canvasHeight);
        const double inset = 3.0;
        return (top + inset, Math.Max(0, height - 2 * inset));
    }

    private void DrawRegions(double canvasWidth, double canvasHeight)
    {
        var (top, height) = LaneBandInset(TimelineLane.Regions, canvasHeight);
        foreach (var region in _enhancement.Regions)
        {
            var startX = Math.Max(0, (region.Start / _totalSeconds) * canvasWidth);
            var endX = Math.Min(canvasWidth, (region.End / _totalSeconds) * canvasWidth);
            var width = Math.Max(MinBandVisualWidthPx, endX - startX);
            var color = ParseHexColor(region.Color) ?? RegionDefaultColor;
            var fill = new Color(160, color.R, color.G, color.B);
            var isSelected = _selectionSet.Contains(region);

            var rect = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(fill),
                Stroke = new SolidColorBrush(isSelected ? Colors.White : color),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = region,
            };
            ToolTip.SetTip(rect, $"{region.Label ?? region.Id} ({region.Start:0.##}s–{region.End:0.##}s)");
            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, top);
            rect.ZIndex = 10;
            rect.PointerEntered += ItemRect_PointerEntered;
            rect.PointerExited += ItemRect_PointerExited;
            rect.PointerPressed += RegionRect_PointerPressed;
            rect.PointerMoved += RegionRect_PointerMoved;
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
            var isSelected = _selectionSet.Contains(item);

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
                rect.PointerEntered += ItemRect_PointerEntered;
                rect.PointerExited += ItemRect_PointerExited;
                rect.PointerPressed += EffectRect_PointerPressed;
                rect.PointerMoved += EffectRect_PointerMoved;
                TimelineCanvas!.Children.Add(rect);
                _timelineVisuals.Add(rect);
            }
        }
    }

    private static bool IsOneShotEffect(string? effectType)
        => effectType == EffectTypes.Flash || effectType == EffectTypes.Subliminal;

    private void DrawHaptics(double canvasWidth, double canvasHeight)
    {
        var (top, height) = LaneBandInset(TimelineLane.Haptics, canvasHeight);
        foreach (var track in _enhancement.HapticTracks)
        {
            foreach (var ev in track.Events)
            {
                var startX = Math.Max(0, (ev.Start / _totalSeconds) * canvasWidth);
                var endX = Math.Min(canvasWidth, ((ev.Start + ev.Duration) / _totalSeconds) * canvasWidth);
                var width = Math.Max(MinBandVisualWidthPx, endX - startX);
                var fill = new Color(160, HapticColor.R, HapticColor.G, HapticColor.B);
                var isSelected = _selectionSet.Contains(ev);

                var rect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = new SolidColorBrush(fill),
                    Stroke = new SolidColorBrush(isSelected ? Colors.White : HapticColor),
                    StrokeThickness = isSelected ? 2.0 : 1.0,
                    Cursor = new Cursor(StandardCursorType.SizeAll),
                    Tag = (track, ev),
                };
                ToolTip.SetTip(rect, $"{ev.PatternName ?? "Haptic"} @ {ev.Start:0.##}s · {ev.Duration:0.##}s");
                Canvas.SetLeft(rect, startX);
                Canvas.SetTop(rect, top);
                rect.ZIndex = 10;
                rect.PointerEntered += ItemRect_PointerEntered;
                rect.PointerExited += ItemRect_PointerExited;
                rect.PointerPressed += HapticRect_PointerPressed;
                rect.PointerMoved += HapticRect_PointerMoved;
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

    private static EdgeHit ClassifyEdgeHit(double posX, double rectWidth)
    {
        if (rectWidth < MinResizableRectPx) return EdgeHit.Body;
        var edge = Math.Min(EdgeResizePx, rectWidth / 3.0);
        if (posX <= edge) return EdgeHit.Start;
        if (posX >= rectWidth - edge) return EdgeHit.End;
        return EdgeHit.Body;
    }

    // ========================================================================
    // Pointer handlers
    // ========================================================================

    private void TimelineCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TimelineCanvas == null) return;
        var point = e.GetCurrentPoint(TimelineCanvas);
        if (!point.Properties.IsLeftButtonPressed) return;

        var position = point.Position;
        var modifiers = e.KeyModifiers;

        // Shift+drag = create region
        if ((modifiers & KeyModifiers.Shift) == KeyModifiers.Shift && _totalSeconds > 0)
        {
            _dragMode = DragMode.CreateRegion;
            _dragCreateStartSec = MouseToSeconds(position);
            StartDragCreatePreview(_dragCreateStartSec);
            e.Handled = true;
            return;
        }

        // Ctrl+drag = rubber-band multi-select
        if ((modifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            _dragMode = DragMode.RubberBand;
            StartRubberBand(position);
            e.Handled = true;
            return;
        }

        // Plain drag on empty canvas = scrub
        SelectNothingOnTimeline();
        _dragMode = DragMode.Scrub;
        SeekToSeconds(MouseToSeconds(position));
        e.Handled = true;
    }

    private void TimelineCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (TimelineCanvas == null) return;
        var point = e.GetCurrentPoint(TimelineCanvas);
        var position = point.Position;

        switch (_dragMode)
        {
            case DragMode.Scrub:
                SeekToSeconds(MouseToSeconds(position));
                break;
            case DragMode.CreateRegion:
                UpdateDragCreatePreview(MouseToSeconds(position));
                break;
            case DragMode.RubberBand:
                UpdateRubberBand(position);
                break;
            case DragMode.DragRegion when _draggedRegion != null:
                {
                    var newStart = Math.Max(0, MouseToSeconds(position) - _regionDragOffsetSec);
                    if (_totalSeconds > 0) newStart = Math.Min(newStart, Math.Max(0, _totalSeconds - _regionDragOriginalLength));
                    _draggedRegion.Start = newStart;
                    _draggedRegion.End = newStart + _regionDragOriginalLength;
                    MarkDirty();
                    RebuildTimeline();
                    PopulateRegionDetail(_draggedRegion);
                }
                break;
            case DragMode.ResizeRegionStart when _draggedRegion != null:
                {
                    var newStart = Math.Max(0, Math.Min(MouseToSeconds(position), _draggedRegion.End - 0.05));
                    _draggedRegion.Start = newStart;
                    MarkDirty();
                    RebuildTimeline();
                    PopulateRegionDetail(_draggedRegion);
                }
                break;
            case DragMode.ResizeRegionEnd when _draggedRegion != null:
                {
                    var newEnd = Math.Min(_totalSeconds, Math.Max(MouseToSeconds(position), _draggedRegion.Start + 0.05));
                    _draggedRegion.End = newEnd;
                    MarkDirty();
                    RebuildTimeline();
                    PopulateRegionDetail(_draggedRegion);
                }
                break;
            case DragMode.DragEffect when _draggedEffect != null:
                {
                    var newStart = Math.Max(0, MouseToSeconds(position) - _effectDragOffsetSec);
                    if (_totalSeconds > 0) newStart = Math.Min(newStart, Math.Max(0, _totalSeconds - _effectDragOriginalDuration));
                    _draggedEffect.Start = newStart;
                    MarkDirty();
                    RebuildTimeline();
                    PopulateRuleDetail(LstRules.SelectedItem as EnhancementRule);
                }
                break;
            case DragMode.ResizeEffectStart when _draggedEffect != null:
                {
                    var oldEnd = _draggedEffect.Start + Math.Max(0, _draggedEffect.Duration);
                    var newStart = Math.Max(0, Math.Min(MouseToSeconds(position), oldEnd - 0.05));
                    _draggedEffect.Duration = oldEnd - newStart;
                    _draggedEffect.Start = newStart;
                    _draggedEffect.EffectDurationMs = (int)Math.Max(50, _draggedEffect.Duration * 1000);
                    MarkDirty();
                    RebuildTimeline();
                    PopulateRuleDetail(LstRules.SelectedItem as EnhancementRule);
                }
                break;
            case DragMode.ResizeEffectEnd when _draggedEffect != null:
                {
                    var newEnd = Math.Min(_totalSeconds, Math.Max(MouseToSeconds(position), _draggedEffect.Start + 0.05));
                    _draggedEffect.Duration = newEnd - _draggedEffect.Start;
                    _draggedEffect.EffectDurationMs = (int)Math.Max(50, _draggedEffect.Duration * 1000);
                    MarkDirty();
                    RebuildTimeline();
                    PopulateRuleDetail(LstRules.SelectedItem as EnhancementRule);
                }
                break;
            case DragMode.DragHaptic when _draggedHaptic != null:
                {
                    var newStart = Math.Max(0, MouseToSeconds(position) - _hapticDragOffsetSec);
                    if (_totalSeconds > 0) newStart = Math.Min(newStart, Math.Max(0, _totalSeconds - _draggedHaptic.Duration));
                    _draggedHaptic.Start = newStart;
                    MarkDirty();
                    RebuildTimeline();
                    PopulateHapticDetail(_draggedHaptic);
                }
                break;
            case DragMode.ResizeHapticStart when _draggedHaptic != null:
                {
                    var endSec = _hapticDragStartSec + _draggedHaptic.Duration;
                    var newStart = Math.Max(0, Math.Min(MouseToSeconds(position), endSec - 0.05));
                    _draggedHaptic.Duration = endSec - newStart;
                    _draggedHaptic.Start = newStart;
                    MarkDirty();
                    RebuildTimeline();
                    PopulateHapticDetail(_draggedHaptic);
                }
                break;
            case DragMode.ResizeHapticEnd when _draggedHaptic != null:
                {
                    var newEnd = Math.Min(_totalSeconds, Math.Max(MouseToSeconds(position), _draggedHaptic.Start + 0.05));
                    _draggedHaptic.Duration = newEnd - _draggedHaptic.Start;
                    MarkDirty();
                    RebuildTimeline();
                    PopulateHapticDetail(_draggedHaptic);
                }
                break;
        }
    }

    private void TimelineCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (TimelineCanvas == null) return;

        switch (_dragMode)
        {
            case DragMode.RubberBand:
                FinishRubberBand(e.GetPosition(TimelineCanvas));
                break;
            case DragMode.CreateRegion:
                FinishDragCreate(MouseToSeconds(e.GetPosition(TimelineCanvas)));
                break;
        }

        _dragMode = DragMode.None;
        _draggedRegion = null;
        _draggedEffect = null;
        _draggedHaptic = null;
        _draggedHapticTrack = null;
        RefreshValidationStatus();
    }

    private void TimelineCanvas_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragMode = DragMode.None;
        if (_rubberBandRect != null)
        {
            try { TimelineCanvas?.Children.Remove(_rubberBandRect); } catch { }
            _rubberBandRect = null;
        }
        if (_dragCreatePreview != null)
        {
            try { TimelineCanvas?.Children.Remove(_dragCreatePreview); } catch { }
            _dragCreatePreview = null;
        }
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
    // Region drag-create
    // ========================================================================

    private void StartDragCreatePreview(double startSec)
    {
        _dragCreatePreview = new Rectangle
        {
            Fill = this.FindResource("DeeperAccentTransparent40Brush") as IBrush ?? new SolidColorBrush(Color.FromArgb(100, 123, 92, 255)),
            Stroke = this.FindResource("DeeperAccentBrush") as IBrush ?? new SolidColorBrush(Color.Parse("#FF7B5CFF")),
            StrokeThickness = 1.5,
        };
        _dragCreatePreview.ZIndex = 60;
        TimelineCanvas!.Children.Add(_dragCreatePreview);
        UpdateDragCreatePreview(startSec);
    }

    private void UpdateDragCreatePreview(double endSec)
    {
        if (_dragCreatePreview == null || TimelineCanvas == null) return;
        var w = TimelineCanvas.Bounds.Width * _zoomFactor;
        var h = TimelineCanvas.Bounds.Height;
        if (w <= 0 || _totalSeconds <= 0) return;

        var lo = Math.Min(_dragCreateStartSec, endSec);
        var hi = Math.Max(_dragCreateStartSec, endSec);
        var leftX = (lo / _totalSeconds) * w;
        var rightX = (hi / _totalSeconds) * w;

        var (regionTop, regionH) = LaneBandInset(TimelineLane.Regions, h);
        _dragCreatePreview.Width = Math.Max(0, rightX - leftX);
        _dragCreatePreview.Height = regionH;
        Canvas.SetLeft(_dragCreatePreview, leftX);
        Canvas.SetTop(_dragCreatePreview, regionTop);
    }

    private void FinishDragCreate(double endSec)
    {
        if (_dragCreatePreview != null)
        {
            TimelineCanvas!.Children.Remove(_dragCreatePreview);
            _dragCreatePreview = null;
        }
        CreateRegion(_dragCreateStartSec, endSec);
    }

    private void CreateRegion(double a, double b)
    {
        var lo = Math.Min(a, b);
        var hi = Math.Max(a, b);
        if (hi - lo < 0.1) return;
        if (_totalSeconds > 0) hi = Math.Min(hi, _totalSeconds);
        lo = Math.Max(0, lo);

        var region = new Region
        {
            Id = NextRegionId(),
            Start = lo,
            End = hi,
            Label = $"Region {_enhancement.Regions.Count + 1}",
            Color = NextRegionColor()
        };
        _enhancement.Regions.Add(region);
        MarkDirty();
        RebuildTimeline();
        LstRegions.SelectedItem = region;
        EditorTabControl.SelectedIndex = 2; // Regions tab
    }

    private string NextRegionId()
    {
        int n = _enhancement.Regions.Count + 1;
        while (true)
        {
            var candidate = "r" + n;
            if (!_enhancement.Regions.Any(r => r.Id == candidate)) return candidate;
            n++;
        }
    }

    private string NextRegionColor() => RegionPalette[_enhancement.Regions.Count % RegionPalette.Length];

    // ========================================================================
    // Rubber-band multi-select
    // ========================================================================

    private void StartRubberBand(Point canvasPt)
    {
        _rubberBandStartPoint = canvasPt;
        _rubberBandRect = null;
    }

    private void UpdateRubberBand(Point canvasPt)
    {
        var dx = Math.Abs(canvasPt.X - _rubberBandStartPoint.X);
        var dy = Math.Abs(canvasPt.Y - _rubberBandStartPoint.Y);
        if (_rubberBandRect == null && dx < 3 && dy < 3) return;

        if (_rubberBandRect == null)
        {
            _rubberBandRect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x68, 0xF0, 0xFF)),
                Stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0xB0, 0xE0, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 2, 2 },
            };
            _rubberBandRect.ZIndex = 60;
            TimelineCanvas!.Children.Add(_rubberBandRect);
        }

        var x = Math.Min(_rubberBandStartPoint.X, canvasPt.X);
        var y = Math.Min(_rubberBandStartPoint.Y, canvasPt.Y);
        var w = Math.Abs(canvasPt.X - _rubberBandStartPoint.X);
        var h = Math.Abs(canvasPt.Y - _rubberBandStartPoint.Y);
        Canvas.SetLeft(_rubberBandRect, x);
        Canvas.SetTop(_rubberBandRect, y);
        _rubberBandRect.Width = w;
        _rubberBandRect.Height = h;
    }

    private void FinishRubberBand(Point canvasPt)
    {
        if (_rubberBandRect != null)
        {
            TimelineCanvas!.Children.Remove(_rubberBandRect);
            _rubberBandRect = null;
        }

        var canvasW = TimelineCanvas!.Bounds.Width * _zoomFactor;
        var canvasH = TimelineCanvas.Bounds.Height;
        if (canvasW <= 0 || canvasH <= 0 || _totalSeconds <= 0) return;

        double xMin = Math.Max(0, Math.Min(_rubberBandStartPoint.X, canvasPt.X));
        double xMax = Math.Min(canvasW, Math.Max(_rubberBandStartPoint.X, canvasPt.X));
        double yMin = Math.Max(0, Math.Min(_rubberBandStartPoint.Y, canvasPt.Y));
        double yMax = Math.Min(canvasH, Math.Max(_rubberBandStartPoint.Y, canvasPt.Y));

        double tMin = (xMin / canvasW) * _totalSeconds;
        double tMax = (xMax / canvasW) * _totalSeconds;

        _selectionSet.Clear();

        var (regionTop, regionHeight) = LaneBand(TimelineLane.Regions, canvasH);
        var (effectsTop, effectsHeight) = LaneBand(TimelineLane.Effects, canvasH);
        var (hapticsTop, hapticsHeight) = LaneBand(TimelineLane.Haptics, canvasH);

        bool RangesOverlap(double a1, double a2, double b1, double b2) => a1 < b2 && a2 > b1;

        if (RangesOverlap(yMin, yMax, regionTop, regionTop + regionHeight))
        {
            foreach (var r in _enhancement.Regions)
                if (RangesOverlap(tMin, tMax, r.Start, r.End))
                    _selectionSet.Add(r);
        }
        if (RangesOverlap(yMin, yMax, effectsTop, effectsTop + effectsHeight))
        {
            foreach (var item in _enhancement.TimelineItems)
            {
                if (item.Kind != TimelineItemKind.Effect || item.EffectType == EffectTypes.Haptic) continue;
                if (RangesOverlap(tMin, tMax, item.Start, item.Start + Math.Max(0, item.Duration)))
                    _selectionSet.Add(item);
            }
        }
        if (RangesOverlap(yMin, yMax, hapticsTop, hapticsTop + hapticsHeight))
        {
            foreach (var track in _enhancement.HapticTracks)
                foreach (var ev in track.Events)
                    if (RangesOverlap(tMin, tMax, ev.Start, ev.Start + ev.Duration))
                        _selectionSet.Add(ev);
        }

        RebuildTimeline();
    }

    // ========================================================================
    // Item selection from timeline
    // ========================================================================

    private void RegionRect_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle r || r.Tag is not Region region) return;
        e.Handled = true;

        var point = e.GetCurrentPoint(r);
        var pos = point.Position;
        var rectWidth = r.Bounds.Width;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;

        HandleSelectionClick(region, ctrl);
        if (ctrl)
        {
            RebuildTimeline();
            return;
        }

        LstRegions.SelectedItem = region;
        EditorTabControl.SelectedIndex = 2; // Regions tab

        _draggedRegion = region;
        _regionDragOriginalLength = Math.Max(0, region.End - region.Start);
        var edge = ClassifyEdgeHit(pos.X, rectWidth);
        _dragMode = edge switch
        {
            EdgeHit.Start => DragMode.ResizeRegionStart,
            EdgeHit.End => DragMode.ResizeRegionEnd,
            _ => DragMode.DragRegion,
        };
        if (_dragMode == DragMode.DragRegion)
            _regionDragOffsetSec = MouseToSeconds(e.GetPosition(TimelineCanvas)) - region.Start;
    }

    private void EffectRect_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle r || r.Tag is not TimelineItem item) return;
        e.Handled = true;

        var point = e.GetCurrentPoint(r);
        var pos = point.Position;
        var rectWidth = r.Bounds.Width;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;

        HandleSelectionClick(item, ctrl);
        if (ctrl)
        {
            RebuildTimeline();
            return;
        }

        SelectRuleForEffect(item);
        EditorTabControl.SelectedIndex = 3; // Rules tab

        _draggedEffect = item;
        _effectDragOriginalDuration = Math.Max(0, item.Duration);
        var edge = ClassifyEdgeHit(pos.X, rectWidth);
        _dragMode = edge switch
        {
            EdgeHit.Start => DragMode.ResizeEffectStart,
            EdgeHit.End => DragMode.ResizeEffectEnd,
            _ => DragMode.DragEffect,
        };
        if (_dragMode == DragMode.DragEffect)
            _effectDragOffsetSec = MouseToSeconds(e.GetPosition(TimelineCanvas)) - item.Start;
    }

    private void EffectDot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Ellipse dot || dot.Tag is not TimelineItem item) return;
        e.Handled = true;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        HandleSelectionClick(item, ctrl);
        if (ctrl)
        {
            RebuildTimeline();
            return;
        }
        SelectRuleForEffect(item);
        EditorTabControl.SelectedIndex = 3; // Rules tab
        _draggedEffect = item;
        _effectDragOriginalDuration = 0;
        _effectDragOffsetSec = MouseToSeconds(e.GetPosition(TimelineCanvas)) - item.Start;
        _dragMode = DragMode.DragEffect;
    }

    private void HapticRect_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle r || r.Tag is not ValueTuple<HapticTrack, HapticEvent> tuple) return;
        e.Handled = true;

        var (track, ev) = tuple;
        var point = e.GetCurrentPoint(r);
        var pos = point.Position;
        var rectWidth = r.Bounds.Width;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;

        HandleSelectionClick(ev, ctrl);
        if (ctrl)
        {
            RebuildTimeline();
            return;
        }

        LstHaptics.SelectedItem = ev;
        EditorTabControl.SelectedIndex = 4; // Haptics tab

        _draggedHaptic = ev;
        _draggedHapticTrack = track;
        _hapticDragOriginalDuration = ev.Duration;
        _hapticDragStartSec = ev.Start;
        var edge = ClassifyEdgeHit(pos.X, rectWidth);
        _dragMode = edge switch
        {
            EdgeHit.Start => DragMode.ResizeHapticStart,
            EdgeHit.End => DragMode.ResizeHapticEnd,
            _ => DragMode.DragHaptic,
        };
        if (_dragMode == DragMode.DragHaptic)
            _hapticDragOffsetSec = MouseToSeconds(e.GetPosition(TimelineCanvas)) - ev.Start;
    }

    private void RulePin_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle rect || rect.Tag is not EnhancementRule rule) return;
        e.Handled = true;
        LstRules.SelectedItem = rule;
        EditorTabControl.SelectedIndex = 3; // Rules tab
    }

    private void HandleSelectionClick(object item, bool ctrl)
    {
        if (ctrl)
        {
            if (!_selectionSet.Add(item)) _selectionSet.Remove(item);
        }
        else
        {
            _selectionSet.Clear();
            _selectionSet.Add(item);
        }
    }

    private void SelectNothingOnTimeline()
    {
        _selectionSet.Clear();
        RebuildTimeline();
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

    // ========================================================================
    // Hover cursor feedback
    // ========================================================================

    private void ItemRect_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is InputElement ie) ie.Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void ItemRect_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is InputElement ie) ie.Cursor = Cursor.Default;
    }

    private void RegionRect_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMode != DragMode.None || sender is not Rectangle r) return;
        var pos = e.GetPosition(r);
        r.Cursor = ClassifyEdgeHit(pos.X, r.Bounds.Width) == EdgeHit.Body
            ? new Cursor(StandardCursorType.SizeAll)
            : new Cursor(StandardCursorType.SizeWestEast);
    }

    private void EffectRect_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMode != DragMode.None || sender is not Rectangle r) return;
        var pos = e.GetPosition(r);
        r.Cursor = ClassifyEdgeHit(pos.X, r.Bounds.Width) == EdgeHit.Body
            ? new Cursor(StandardCursorType.SizeAll)
            : new Cursor(StandardCursorType.SizeWestEast);
    }

    private void HapticRect_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMode != DragMode.None || sender is not Rectangle r) return;
        var pos = e.GetPosition(r);
        r.Cursor = ClassifyEdgeHit(pos.X, r.Bounds.Width) == EdgeHit.Body
            ? new Cursor(StandardCursorType.SizeAll)
            : new Cursor(StandardCursorType.SizeWestEast);
    }

    private static Color? ParseHexColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return Color.Parse(hex); }
        catch { return null; }
    }
}
