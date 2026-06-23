using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaLayout = global::Avalonia.Layout;
using IOPath = System.IO.Path;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Help;
using ConditioningControlPanel.Core.Services.Sessions;

using AvaPoint = global::Avalonia.Point;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the timeline-based session editor.
/// </summary>
public partial class SessionEditorWindow : Window
{
    private readonly TimelineSession _session;
    private readonly SessionFileService _fileService;

    // Drag-drop state (from feature palette)
    private bool _isDraggingFeature;
    private Border? _draggedFeatureIcon;
    private string? _draggedFeatureId;

    // Timeline icon drag state
    private bool _isTimelineDragging;
    private Border? _draggedTimelineIcon;
    private TimelineEvent? _draggedEvent;
    private AvaPoint _dragStartPoint;
    private double _dragStartCanvasLeft;
    private int _dragOriginalMinute;

    // Segment (bar) drag state
    private bool _isSegmentDragging;
    private Border? _draggedBar;
    private TimelineEvent? _draggedStartEvent;
    private TimelineEvent? _draggedStopEvent;
    private int _segmentDragOriginalStartMinute;
    private int _segmentDragOriginalStopMinute;
    private double _segmentDragStartX;

    private readonly IDialogService? _dialogService;

    public Session? ResultSession { get; private set; }
    public bool? DialogResult { get; set; }

    public SessionEditorWindow() : this(null) { }

    public SessionEditorWindow(Session? existingSession)
    {
        InitializeComponent();

        _dialogService = App.Services?.GetService<IDialogService>();

        var env = App.Services?.GetService<IAppEnvironment>()
                  ?? throw new InvalidOperationException("IAppEnvironment is required for the session editor.");
        _fileService = new SessionFileService(env);

        // Canvas-level pointer handlers for smooth dragging
        CanvasTimeline.PointerMoved += CanvasTimeline_PointerMoved;
        CanvasTimeline.PointerReleased += CanvasTimeline_PointerReleased;
        CanvasTimeline.PointerPressed += CanvasTimeline_PointerPressed;

        if (existingSession != null)
        {
            _session = TimelineSession.FromSession(existingSession);
            TxtSessionName.Text = _session.Name;
            TxtDescription.Text = _session.Description;
            SliderDuration.Value = _session.DurationMinutes;
        }
        else
        {
            _session = new TimelineSession();
        }

        InitializeFeatureIcons();
        RefreshTimeline();
        RefreshStats();
    }

    #region Window Chrome

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Close popup if clicking outside of it
        if (SettingsPopup.IsOpen)
        {
            var pos = e.GetPosition(FeatureSettings);
            var bounds = FeatureSettings.Bounds;
            if (!bounds.Contains(pos))
            {
                SettingsPopup.IsOpen = false;
            }
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void BtnMinimize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        ResultSession = null;
        DialogResult = false;
        Close(false);
    }

    private void BtnHelp_Click(object? sender, RoutedEventArgs e)
    {
        var content = HelpContentService.GetContent("SessionEditor");
        if (content.HasClip)
        {
            HelpVideoWindow.Show(content, this);
            return;
        }
        TutorialOverlay.IsVisible = true;
    }

    private void TutorialOverlay_Close(object? sender, RoutedEventArgs e)
    {
        TutorialOverlay.IsVisible = false;
    }

    #endregion

    #region Feature Icons

    private void InitializeFeatureIcons()
    {
        var features = FeatureDefinition.GetAllFeatures();

        foreach (var feature in features)
        {
            var panel = GetCategoryPanel(feature.Category);
            if (panel == null) continue;

            var icon = CreateFeatureIcon(feature);
            panel.Children.Add(icon);
        }
    }

    private Panel? GetCategoryPanel(FeatureCategory category)
    {
        return category switch
        {
            FeatureCategory.Audio => AudioFeatures,
            FeatureCategory.Video => VideoFeatures,
            FeatureCategory.Overlays => OverlayFeatures,
            FeatureCategory.Interactive => InteractiveFeatures,
            FeatureCategory.Extras => ExtrasFeatures,
            _ => null
        };
    }

    private Border CreateFeatureIcon(FeatureDefinition feature)
    {
        var border = new Border
        {
            Background = (SolidColorBrush?)Resources["PanelBgBrush"] ?? new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["ElevatedSurface"]!),
            CornerRadius = new CornerRadius(7),
            Width = 68,
            Height = 68,
            Margin = new Thickness(4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = feature.Id
        };
        ToolTip.SetTip(border, $"{feature.Name}\n{GetFeatureDescription(feature)}\n\nDrag to timeline to add a segment");

        var grid = new Grid();
        var content = CreateFeatureIconContent(feature, 60);
        grid.Children.Add(content);
        border.Child = grid;

        // Drag events
        border.PointerPressed += FeatureIcon_PointerPressed;
        border.PointerMoved += FeatureIcon_PointerMoved;
        border.PointerReleased += FeatureIcon_PointerReleased;

        return border;
    }

    private string GetFeatureDescription(FeatureDefinition feature)
    {
        return feature.Id switch
        {
            "audio_whispers" => "Plays audio whispers throughout the session",
            "mind_wipe" => "Powerful audio effect for deep immersion",
            "flash" => "Flashes images on screen periodically",
            "mandatory_videos" => "Plays mandatory video clips",
            "subliminal" => "Shows subliminal text messages",
            "bouncing_text" => "Displays bouncing text across the screen",
            "pink_filter" => "Applies a pink color filter overlay",
            "spiral" => "Shows a hypnotic spiral overlay",
            "brain_drain" => "Intense visual distortion effect",
            "bubbles" => "Floating interactive bubbles",
            "lock_cards" => "Interactive lock card challenges",
            "bubble_count" => "Bubble counting mini-game",
            "corner_gif" => "Displays a GIF in the corner",
            _ => "An effect for your session"
        };
    }

    private Control CreateFeatureIconContent(FeatureDefinition feature, double size)
    {
        if (!string.IsNullOrEmpty(feature.ImagePath))
        {
            try
            {
                var bitmap = AvaloniaBitmapHelper.LoadResource(feature.ImagePath.Replace('\\', '/'));

                if (bitmap != null)
                {
                    return new Border
                    {
                        Width = size,
                        Height = size,
                        CornerRadius = new CornerRadius(size * 0.15),
                        Background = new ImageBrush
                        {
                            Source = bitmap,
                            Stretch = Stretch.UniformToFill
                        }
                    };
                }
            }
            catch { /* Fall back to emoji */ }
        }

        return new TextBlock
        {
            Text = feature.Icon,
            FontSize = size,
            HorizontalAlignment = AvaloniaLayout.HorizontalAlignment.Center,
            VerticalAlignment = AvaloniaLayout.VerticalAlignment.Center
        };
    }

    #endregion

    #region Feature Palette Drag

    private void FeatureIcon_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;

        _draggedFeatureIcon = border;
        _draggedFeatureId = border.Tag as string;
        _isDraggingFeature = false;
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void FeatureIcon_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedFeatureIcon == null || _draggedFeatureId == null) return;
        if (!e.GetCurrentPoint(_draggedFeatureIcon).Properties.IsLeftButtonPressed)
        {
            ResetFeatureDrag();
            return;
        }

        _isDraggingFeature = true;
    }

    private void FeatureIcon_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        if (_isDraggingFeature && _draggedFeatureId != null)
        {
            var pos = e.GetPosition(CanvasTimeline);
            var dropBounds = CanvasTimeline.Bounds;
            if (dropBounds.Contains(pos))
            {
                AddFeatureAtPosition(_draggedFeatureId, pos.X);
            }
        }
        ResetFeatureDrag();
    }

    private void ResetFeatureDrag()
    {
        _isDraggingFeature = false;
        _draggedFeatureIcon = null;
        _draggedFeatureId = null;
    }

    private async void AddFeatureAtPosition(string featureId, double x)
    {
        var startMinute = PositionToMinute(x);
        var defaultDuration = Math.Min(10, _session.DurationMinutes - startMinute);
        if (defaultDuration < 1) defaultDuration = 1;
        var endMinute = Math.Min(startMinute + defaultDuration, _session.DurationMinutes);

        if (_session.IsOverlapping(featureId, startMinute, endMinute))
        {
            var lastEndMinute = _session.GetLastSegmentEndMinute(featureId);
            if (lastEndMinute >= 0)
            {
                startMinute = lastEndMinute + 1;
                endMinute = Math.Min(startMinute + defaultDuration, _session.DurationMinutes);

                if (startMinute >= _session.DurationMinutes)
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowMessageAsync(
                            Loc.Get("title_timeline_full"),
                            Loc.Get("msg_no_more_room_in_the_timeline_for_this_effect"),
                            DialogSeverity.Warning);
                    }
                    return;
                }
                if (endMinute <= startMinute)
                {
                    endMinute = Math.Min(startMinute + 1, _session.DurationMinutes);
                }
            }
        }

        var startEvt = _session.AddStartEvent(featureId, startMinute);
        _session.AddStopEvent(startEvt, endMinute);

        RefreshTimeline();
        RefreshStats();
    }

    #endregion

    #region Timeline Rendering

    private void RefreshTimeline()
    {
        RenderMarkers();
        RenderEvents();

        TxtTimelineHint.IsVisible = !_session.Events.Any();
    }

    private void RenderMarkers()
    {
        CanvasMarkers.Children.Clear();

        var duration = _session.DurationMinutes;
        var width = CanvasMarkers.Bounds.Width > 0 ? CanvasMarkers.Bounds.Width : 800;

        int interval = duration <= 30 ? 5 : (duration <= 60 ? 10 : (duration <= 120 ? 15 : 30));

        for (int min = 0; min <= duration; min += interval)
        {
            var x = MinuteToPosition(min, width);

            var line = new Line
            {
                StartPoint = new AvaPoint(x, 15),
                EndPoint = new AvaPoint(x, 20),
                Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                StrokeThickness = 1
            };
            CanvasMarkers.Children.Add(line);

            string markerText;
            if (duration > 60)
            {
                int hours = min / 60;
                int mins = min % 60;
                markerText = hours > 0 ? $"{hours}:{mins:D2}" : mins.ToString();
            }
            else
            {
                markerText = min.ToString();
            }

            var text = new TextBlock
            {
                Text = markerText,
                Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                FontSize = 10
            };
            Canvas.SetLeft(text, x - 8);
            Canvas.SetTop(text, 0);
            CanvasMarkers.Children.Add(text);
        }
    }

    private const int TimelineRowHeight = 45;

    private void RenderEvents()
    {
        CanvasTimeline.Children.Clear();
        CanvasTimeline.Children.Add(TxtTimelineHint);

        var width = CanvasTimeline.Bounds.Width > 0 ? CanvasTimeline.Bounds.Width : 800;

        var featureIds = _session.Events
            .Select(e => e.FeatureId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var featureRows = new Dictionary<string, int>();
        for (int i = 0; i < featureIds.Count; i++)
        {
            featureRows[featureIds[i]] = i;
        }

        var requiredHeight = Math.Max(35, featureIds.Count * TimelineRowHeight + 15);
        CanvasTimeline.Height = requiredHeight;

        foreach (var evt in _session.Events.Where(e => e.EventType == TimelineEventType.Start).OrderBy(e => e.Minute))
        {
            var feature = FeatureDefinition.GetById(evt.FeatureId);
            if (feature == null) continue;

            if (!featureRows.TryGetValue(evt.FeatureId, out var row))
                continue;

            var rowY = 4 + row * TimelineRowHeight;

            var startX = MinuteToPosition(evt.Minute, width);
            var stopEvt = _session.GetPairedStopEvent(evt);
            var endX = stopEvt != null
                ? MinuteToPosition(stopEvt.Minute, width)
                : MinuteToPosition(_session.DurationMinutes, width);

            var bar = new Border
            {
                Width = Math.Max(endX - startX, 10),
                Height = 11,
                Background = new SolidColorBrush(Color.FromArgb(100, 255, 105, 180)),
                CornerRadius = new CornerRadius(3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = evt.Id
            };
            ToolTip.SetTip(bar, $"{feature.Name}\n{evt.Minute} - {stopEvt?.Minute ?? _session.DurationMinutes} min\n\nDrag to move • Right-click to edit");
            bar.PointerPressed += SegmentBar_PointerPressed;
            bar.PointerReleased += SegmentBar_PointerReleased;
            Canvas.SetLeft(bar, startX);
            Canvas.SetTop(bar, rowY + 2);
            CanvasTimeline.Children.Add(bar);

            var startIcon = CreateTimelineIcon(evt, feature, true);
            Canvas.SetLeft(startIcon, startX - 19);
            Canvas.SetTop(startIcon, rowY - 15);
            CanvasTimeline.Children.Add(startIcon);

            if (stopEvt != null)
            {
                var stopIcon = CreateTimelineIcon(stopEvt, feature, false);
                Canvas.SetLeft(stopIcon, endX - 19);
                Canvas.SetTop(stopIcon, rowY - 15);
                CanvasTimeline.Children.Add(stopIcon);
            }
        }
    }

    private Border CreateTimelineIcon(TimelineEvent evt, FeatureDefinition feature, bool isStart)
    {
        var border = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            Tag = evt.Id,
            VerticalAlignment = AvaloniaLayout.VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 3)
        };
        ToolTip.SetTip(border, $"{feature.Name} - {(isStart ? "Start" : "Stop")} at {evt.Minute} min\nDrag to move • Right-click to edit");

        var content = CreateFeatureIconContent(feature, 32);
        border.Child = content;

        border.PointerPressed += TimelineIcon_PointerPressed;
        border.PointerReleased += TimelineIcon_PointerReleased;

        return border;
    }

    #endregion

    #region Timeline Pointer Handlers

    private void CanvasTimeline_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Right-click on empty canvas is ignored here; handled by icon/bar right press.
    }

    private void CanvasTimeline_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedTimelineIcon != null && _draggedEvent != null)
        {
            HandleIconDrag(e);
            return;
        }

        if (_draggedBar != null && _draggedStartEvent != null)
        {
            HandleSegmentDrag(e);
        }
    }

    private void CanvasTimeline_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);

        if (_isTimelineDragging)
        {
            RefreshTimeline();
            RefreshStats();
        }

        if (_isSegmentDragging)
        {
            if (_draggedBar != null) _draggedBar.Opacity = 1.0;
            RefreshTimeline();
            RefreshStats();
        }

        ResetTimelineDrag();
        ResetSegmentDrag();
    }

    private void TimelineIcon_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;

        if (e.GetCurrentPoint(border).Properties.IsRightButtonPressed)
        {
            if (border.Tag is string eventId)
            {
                var evt = _session.Events.FirstOrDefault(ev => ev.Id == eventId);
                if (evt != null)
                {
                    ShowFeatureSettingsPopup(evt);
                }
            }
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;

        var eventId2 = border.Tag as string;
        if (eventId2 == null) return;

        var evt2 = _session.Events.FirstOrDefault(ev => ev.Id == eventId2);
        if (evt2 == null) return;

        _draggedTimelineIcon = border;
        _draggedEvent = evt2;
        _dragStartPoint = e.GetPosition(CanvasTimeline);
        _dragStartCanvasLeft = Canvas.GetLeft(border);
        _dragOriginalMinute = evt2.Minute;
        _isTimelineDragging = false;

        e.Pointer.Capture(CanvasTimeline);
        e.Handled = true;
    }

    private void TimelineIcon_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Canvas-level release handles cleanup.
    }

    private void HandleIconDrag(PointerEventArgs e)
    {
        if (_draggedTimelineIcon == null || _draggedEvent == null) return;

        var currentPos = e.GetPosition(CanvasTimeline);
        var delta = currentPos.X - _dragStartPoint.X;

        if (!_isTimelineDragging && Math.Abs(delta) > 5)
        {
            _isTimelineDragging = true;
        }

        if (_isTimelineDragging)
        {
            var newLeft = _dragStartCanvasLeft + delta;
            newLeft = Math.Max(0, Math.Min(newLeft, CanvasTimeline.Bounds.Width - 20));

            Canvas.SetLeft(_draggedTimelineIcon, newLeft);

            var newMinute = PositionToMinute(newLeft + 10);
            ApplyTimelineDrag(_draggedEvent, newMinute);
            UpdateBarVisual(_draggedEvent);
        }
    }

    private void SegmentBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border bar) return;

        if (e.GetCurrentPoint(bar).Properties.IsRightButtonPressed)
        {
            if (bar.Tag is string startEventId)
            {
                var evt = _session.Events.FirstOrDefault(ev => ev.Id == startEventId);
                if (evt != null)
                {
                    ShowFeatureSettingsPopup(evt);
                }
            }
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;

        var startEventId2 = bar.Tag as string;
        if (startEventId2 == null) return;

        var startEvt = _session.Events.FirstOrDefault(ev => ev.Id == startEventId2);
        if (startEvt == null) return;

        var stopEvt = _session.GetPairedStopEvent(startEvt);

        _draggedBar = bar;
        _draggedStartEvent = startEvt;
        _draggedStopEvent = stopEvt;
        _segmentDragOriginalStartMinute = startEvt.Minute;
        _segmentDragOriginalStopMinute = stopEvt?.Minute ?? _session.DurationMinutes;
        _segmentDragStartX = e.GetPosition(CanvasTimeline).X;
        _isSegmentDragging = false;

        e.Pointer.Capture(CanvasTimeline);
        e.Handled = true;
    }

    private void SegmentBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Canvas-level release handles cleanup.
    }

    private void HandleSegmentDrag(PointerEventArgs e)
    {
        if (_draggedBar == null || _draggedStartEvent == null) return;

        var currentX = e.GetPosition(CanvasTimeline).X;
        var deltaX = currentX - _segmentDragStartX;

        if (!_isSegmentDragging && Math.Abs(deltaX) > 5)
        {
            _isSegmentDragging = true;
            _draggedBar.Opacity = 0.7;
        }

        if (_isSegmentDragging)
        {
            var width = CanvasTimeline.Bounds.Width > 0 ? CanvasTimeline.Bounds.Width : 800;

            var originalStartX = MinuteToPosition(_segmentDragOriginalStartMinute, width);
            var newStartX = originalStartX + deltaX;
            var newStartMinute = PositionToMinute(newStartX);

            var duration = _segmentDragOriginalStopMinute - _segmentDragOriginalStartMinute;

            newStartMinute = Math.Max(0, newStartMinute);
            if (_draggedStopEvent != null)
            {
                newStartMinute = Math.Min(newStartMinute, _session.DurationMinutes - duration);
            }

            var newStopMinute = newStartMinute + duration;

            if (_session.IsOverlapping(_draggedStartEvent.FeatureId, newStartMinute, newStopMinute, _draggedStartEvent.Id))
            {
                return;
            }

            _draggedStartEvent.Minute = newStartMinute;
            if (_draggedStopEvent != null)
                _draggedStopEvent.Minute = newStopMinute;

            var startX = MinuteToPosition(newStartMinute, width);
            var endX = MinuteToPosition(newStopMinute, width);

            Canvas.SetLeft(_draggedBar, startX);

            foreach (var child in CanvasTimeline.Children)
            {
                if (child is Border icon && icon.Tag is string iconEventId)
                {
                    if (iconEventId == _draggedStartEvent.Id)
                    {
                        Canvas.SetLeft(icon, startX - 10);
                    }
                    else if (_draggedStopEvent != null && iconEventId == _draggedStopEvent.Id)
                    {
                        Canvas.SetLeft(icon, endX - 10);
                    }
                }
            }
        }
    }

    private void UpdateBarVisual(TimelineEvent evt)
    {
        var width = CanvasTimeline.Bounds.Width > 0 ? CanvasTimeline.Bounds.Width : 800;

        TimelineEvent? startEvt = null;
        TimelineEvent? stopEvt = null;

        if (evt.EventType == TimelineEventType.Start)
        {
            startEvt = evt;
            stopEvt = _session.GetPairedStopEvent(evt);
        }
        else
        {
            startEvt = _session.Events.FirstOrDefault(e => e.EventType == TimelineEventType.Start && e.PairedEventId == evt.Id);
            stopEvt = evt;
        }

        if (startEvt == null) return;

        foreach (var child in CanvasTimeline.Children)
        {
            if (child is Border bar && bar.Tag as string == startEvt.Id)
            {
                var startX = MinuteToPosition(startEvt.Minute, width);
                var endX = stopEvt != null
                    ? MinuteToPosition(stopEvt.Minute, width)
                    : MinuteToPosition(_session.DurationMinutes, width);

                Canvas.SetLeft(bar, startX);
                bar.Width = Math.Max(endX - startX, 10);
                break;
            }
        }
    }

    private void ApplyTimelineDrag(TimelineEvent evt, int newMinute)
    {
        newMinute = Math.Max(0, Math.Min(newMinute, _session.DurationMinutes));

        int startMinute, endMinute;

        if (evt.EventType == TimelineEventType.Start)
        {
            var stopEvt = _session.GetPairedStopEvent(evt);
            if (stopEvt == null) return;
            startMinute = newMinute;
            endMinute = stopEvt.Minute;

            if (startMinute >= endMinute)
            {
                startMinute = Math.Max(0, endMinute - 1);
            }
        }
        else
        {
            var startEvt = _session.Events.FirstOrDefault(e => e.EventType == TimelineEventType.Start && e.PairedEventId == evt.Id);
            if (startEvt == null) return;
            startMinute = startEvt.Minute;
            endMinute = newMinute;

            if (endMinute <= startMinute)
            {
                endMinute = Math.Min(_session.DurationMinutes, startMinute + 1);
            }
        }

        if (_session.IsOverlapping(evt.FeatureId, startMinute, endMinute, evt.Id))
        {
            return;
        }

        evt.Minute = newMinute;
    }

    private void ResetTimelineDrag()
    {
        _isTimelineDragging = false;
        _draggedTimelineIcon = null;
        _draggedEvent = null;
    }

    private void ResetSegmentDrag()
    {
        _isSegmentDragging = false;
        _draggedBar = null;
        _draggedStartEvent = null;
        _draggedStopEvent = null;
    }

    private const double TimelinePadding = 45;

    private double MinuteToPosition(int minute, double width)
    {
        var usableWidth = width - (TimelinePadding * 2);
        return TimelinePadding + (minute / (double)_session.DurationMinutes) * usableWidth;
    }

    private int PositionToMinute(double x)
    {
        var width = CanvasTimeline.Bounds.Width > 0 ? CanvasTimeline.Bounds.Width : 800;
        var usableWidth = width - (TimelinePadding * 2);
        var adjustedX = x - TimelinePadding;
        var minute = (int)Math.Round((adjustedX / usableWidth) * _session.DurationMinutes);
        return Math.Max(0, Math.Min(minute, _session.DurationMinutes));
    }

    #endregion

    #region Settings Popup

    private void ShowFeatureSettingsPopup(TimelineEvent evt)
    {
        FeatureSettings.LoadEvent(evt, _session.DurationMinutes, _session);

        FeatureSettings.SettingsChanged -= OnSettingsChanged;
        FeatureSettings.SettingsChanged += OnSettingsChanged;

        FeatureSettings.DeleteRequested -= OnDeleteRequested;
        FeatureSettings.DeleteRequested += OnDeleteRequested;

        FeatureSettings.CloseRequested -= OnPopupCloseRequested;
        FeatureSettings.CloseRequested += OnPopupCloseRequested;

        SettingsPopup.PlacementTarget = this;
        SettingsPopup.IsOpen = true;
    }

    private void OnSettingsChanged(object? sender, TimelineEvent evt)
    {
        RefreshTimeline();
        RefreshStats();
    }

    private void OnDeleteRequested(object? sender, TimelineEvent evt)
    {
        SettingsPopup.IsOpen = false;
        _session.RemoveEvent(evt);
        RefreshTimeline();
        RefreshStats();
    }

    private void OnPopupCloseRequested(object? sender, EventArgs e)
    {
        SettingsPopup.IsOpen = false;
    }

    #endregion

    #region Stats

    private void RefreshStats()
    {
        var xp = _session.CalculateXP();
        var difficulty = _session.CalculateDifficulty();
        var difficultyText = _session.GetDifficultyText();
        var difficultyColor = _session.GetDifficultyColor();

        TxtXP.Text = Loc.GetF("session_xp_amount", xp);
        TxtDifficulty.Text = difficultyText;
        TxtDifficulty.Foreground = new SolidColorBrush(Color.Parse(difficultyColor));
        TxtDuration.Text = Loc.GetF("session_duration_min", _session.DurationMinutes);
    }

    private void SliderDuration_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_session == null) return;

        _session.DurationMinutes = (int)e.NewValue;
        TxtDurationValue.Text = Loc.GetF("session_duration_min", _session.DurationMinutes);

        foreach (var evt in _session.Events.Where(ev => ev.Minute > _session.DurationMinutes).ToList())
        {
            evt.Minute = _session.DurationMinutes;
        }

        var collapsedStarts = _session.Events
            .Where(e => e.EventType == TimelineEventType.Start && e.PairedEventId != null)
            .Where(start =>
            {
                var stop = _session.Events.FirstOrDefault(e => e.Id == start.PairedEventId);
                return stop != null && start.Minute >= stop.Minute;
            })
            .ToList();
        foreach (var start in collapsedStarts)
        {
            var stop = _session.Events.FirstOrDefault(e => e.Id == start.PairedEventId);
            _session.Events.Remove(start);
            if (stop != null) _session.Events.Remove(stop);
        }

        RefreshTimeline();
        RefreshStats();
    }

    #endregion

    #region Buttons

    private async void BtnImport_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = App.Services?.GetService<IDialogService>();
        if (dialog == null) return;

        var files = await dialog.ShowOpenFileDialogAsync(
            Loc.Get("title_import_session"),
            new[] { new FileFilter("Session Files", new[] { "session.json" }) });

        if (files == null || files.Count == 0) return;
        var fileName = files[0];

        if (!_fileService.ValidateSessionFile(fileName, out var error))
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_import_error"),
                    Loc.GetF("msg_invalid_session_file", error),
                    DialogSeverity.Error);
            }
            return;
        }

        var definition = _fileService.ImportSession(fileName);
        if (definition == null)
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_import_error"),
                    Loc.Get("msg_failed_to_import_session"),
                    DialogSeverity.Error);
            }
            return;
        }

        var imported = definition.ToSession();
        var timelineSession = TimelineSession.FromSession(imported);

        _session.Id = timelineSession.Id;
        _session.Name = timelineSession.Name;
        _session.Icon = timelineSession.Icon;
        _session.Description = timelineSession.Description;
        _session.DurationMinutes = timelineSession.DurationMinutes;
        _session.Events.Clear();
        _session.Events.AddRange(timelineSession.Events);
        _session.SubliminalPhrases = new List<string>(timelineSession.SubliminalPhrases);
        _session.BouncingTextPhrases = new List<string>(timelineSession.BouncingTextPhrases);

        TxtSessionName.Text = _session.Name;
        TxtDescription.Text = _session.Description;
        SliderDuration.Value = _session.DurationMinutes;

        RefreshTimeline();
        RefreshStats();

        if (_dialogService != null)
        {
            await _dialogService.ShowMessageAsync(
                Loc.Get("title_import_successful"),
                Loc.GetF("msg_imported_session", _session.Name),
                DialogSeverity.Info);
        }
    }

    private async void BtnExport_Click(object? sender, RoutedEventArgs e)
    {
        _session.Name = TxtSessionName.Text ?? "";
        _session.Description = TxtDescription.Text ?? "";

        var dialog = App.Services?.GetService<IDialogService>();
        if (dialog == null) return;

        var fileName = SessionFileService.GetExportFileName(_session.ToSession());
        var path = await dialog.ShowSaveFileDialogAsync(
            Loc.Get("title_export_session"),
            new[] { new FileFilter("Session Files", new[] { "session.json" }) },
            fileName);

        if (string.IsNullOrEmpty(path)) return;

        var session = _session.ToSession();
        _fileService.ExportSession(session, path);
        if (_dialogService != null)
        {
            await _dialogService.ShowMessageAsync(
                Loc.Get("title_export_successful"),
                Loc.GetF("msg_session_exported_to", path),
                DialogSeverity.Info);
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        ResultSession = null;
        DialogResult = false;
        Close(false);
    }

    private async void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        _session.Name = TxtSessionName.Text ?? "";
        _session.Description = TxtDescription.Text ?? "";

        if (string.IsNullOrWhiteSpace(_session.Name))
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_validation_error"),
                    Loc.Get("msg_please_enter_a_session_name"),
                    DialogSeverity.Warning);
            }
            return;
        }

        ResultSession = _session.ToSession();
        DialogResult = true;
        Close(true);
    }

    #endregion

    private void Timeline_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (_isDraggingFeature && TimelineDropZone != null)
        {
            TimelineDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 105, 180));
            TimelineDropZone.BorderThickness = new Thickness(2);
        }
    }

    private void Timeline_PointerExited(object? sender, PointerEventArgs e)
    {
        if (TimelineDropZone != null)
        {
            TimelineDropZone.BorderBrush = new SolidColorBrush(Color.Parse("#353555"));
            TimelineDropZone.BorderThickness = new Thickness(0);
        }
    }
}
