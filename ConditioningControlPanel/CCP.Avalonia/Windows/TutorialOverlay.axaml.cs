using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Localization;

using Animation = global::Avalonia.Animation.Animation;
using KeyFrame = global::Avalonia.Animation.KeyFrame;
using FillMode = global::Avalonia.Animation.FillMode;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the interactive tutorial overlay.
/// </summary>
public partial class TutorialOverlay : Window
{
    private readonly object? _tutorialService;
    private Window _targetWindow;

    private TutorialStep? _subscribedStep;
    private Button? _subButton;
    private TextBox? _subTextBox;
    private SelectingItemsControl? _subSelector;
    private Slider? _subSlider;
    private EventHandler<RoutedEventArgs>? _subButtonHandler;
    private EventHandler<TextChangedEventArgs>? _subTextHandler;
    private EventHandler<SelectionChangedEventArgs>? _subSelectionHandler;
    private EventHandler<RangeBaseValueChangedEventArgs>? _subSliderHandler;
    private bool _advanceFiredThisStep;
    private bool _thisOverlayCompleted;

    private DispatcherTimer? _spotlightDelayTimer;

    public TutorialOverlay(Window targetWindow, object tutorialService)
    {
        InitializeComponent();

        _targetWindow = targetWindow;
        _tutorialService = tutorialService;

        try
        {
            dynamic? ts = _tutorialService;
            if (ts != null)
            {
                ts.StepChanged += (EventHandler<TutorialStep>)OnStepChanged;
                ts.TutorialCompleted += (EventHandler)OnTutorialCompleted;
            }
        }
        catch { }

        AttachToTarget(_targetWindow);

        Opacity = 0;
        Loaded += async (s, e) =>
        {
            UpdateOverlayPosition();
            await RunFadeAnimation(0, 1, TimeSpan.FromMilliseconds(300));

            try
            {
                dynamic? ts = _tutorialService;
                var currentStep = ts?.CurrentStep as TutorialStep;
                if (currentStep != null)
                {
                    UpdateStep(currentStep);
                }
            }
            catch { }
        };
    }

    /// <summary>
    /// Required parameterless constructor for Avalonia designer/build.
    /// </summary>
    public TutorialOverlay()
    {
        InitializeComponent();
        _targetWindow = this;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            try
            {
                dynamic? ts = _tutorialService;
                ts?.Skip();
            }
            catch { }
            e.Handled = true;
        }
    }

    public void RetargetToWindow(Window newWindow)
    {
        if (newWindow == null || newWindow == _targetWindow) return;

        DetachFromTarget(_targetWindow);
        _targetWindow = newWindow;
        AttachToTarget(newWindow);
        UpdateOverlayPosition();

        try
        {
            dynamic? ts = _tutorialService;
            var currentStep = ts?.CurrentStep as TutorialStep;
            if (currentStep != null)
            {
                UpdateStep(currentStep);
            }
        }
        catch { }
    }

    private void AttachToTarget(Window w)
    {
        try
        {
            w.PositionChanged += OnTargetMoved;
            w.Resized += OnTargetResized;
            w.Closed += OnTargetClosed;
        }
        catch { }
    }

    private void DetachFromTarget(Window w)
    {
        try
        {
            w.PositionChanged -= OnTargetMoved;
            w.Resized -= OnTargetResized;
            w.Closed -= OnTargetClosed;
        }
        catch { }
    }

    private void OnTargetMoved(object? sender, PixelPointEventArgs e) => UpdateOverlayPosition();
    private void OnTargetResized(object? sender, WindowResizedEventArgs e) => UpdateOverlayPosition();

    private void OnTargetClosed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_thisOverlayCompleted) return;
            try
            {
                dynamic? ts = _tutorialService;
                if (ts?.IsActive != true) return;

                var current = ts?.CurrentStep as TutorialStep;
                if (current != null && !string.IsNullOrEmpty(current.TargetWindowTypeName))
                {
                    return;
                }

                var steps = ts?.CurrentSteps as IReadOnlyList<TutorialStep>;
                int idx = ts?.CurrentStepIndex ?? -1;
                if (steps != null && idx + 1 < steps.Count)
                {
                    var next = steps[idx + 1];
                    if (!string.IsNullOrEmpty(next?.TargetWindowTypeName))
                    {
                        return;
                    }
                }

                ts?.Skip();
            }
            catch { }
        });
    }

    private void UpdateOverlayPosition()
    {
        try
        {
            var screen = this.Screens?.ScreenFromWindow(_targetWindow)
                ?? this.Screens?.Primary;

            if (screen == null)
            {
                var provider = App.Services?.GetService<IScreenProvider>();
                var info = provider?.GetAllScreens().FirstOrDefault();
                if (info != null)
                {
                    Position = new PixelPoint((int)info.Bounds.X, (int)info.Bounds.Y);
                    Width = info.Bounds.Width / info.Scaling;
                    Height = info.Bounds.Height / info.Scaling;
                }
            }
            else
            {
                Position = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y);
                Width = screen.Bounds.Width / screen.Scaling;
                Height = screen.Bounds.Height / screen.Scaling;
            }
        }
        catch { }

        try
        {
            dynamic? ts = _tutorialService;
            var currentStep = ts?.CurrentStep as TutorialStep;
            if (currentStep != null && IsLoaded)
            {
                UpdateStep(currentStep);
            }
        }
        catch { }
    }

    private void OnStepChanged(object? sender, TutorialStep step)
    {
        UpdateStep(step);
    }

    private void OnTutorialCompleted(object? sender, EventArgs e)
    {
        DetachAllSubscriptions();
        FadeOutAndClose();
    }

    private void UpdateStep(TutorialStep step)
    {
        UnsubscribeAdvanceTrigger();

        if (!string.IsNullOrEmpty(step.TargetWindowTypeName) &&
            _targetWindow.GetType().Name != step.TargetWindowTypeName)
        {
            var w = FindWindowByTypeName(step.TargetWindowTypeName);
            if (w != null)
            {
                RetargetToWindow(w);
                return;
            }

            TxtStepCounter.Text = string.Format(Loc.Get("window_tutorial_overlay_step_counter_fmt"), GetCurrentStepIndex() + 1, GetTotalSteps());
            TxtIcon.Text = step.Icon;
            TxtTitle.Text = step.Title;
            TxtDescription.Text = step.Description;
            BtnSupport.IsVisible = false;
            BtnSkip.IsVisible = !step.IsFollowUpCard;
            BtnSkipStep.IsVisible = false;
            BtnNext.IsVisible = false;
            BtnPrevious.IsVisible = false;
            FollowUpPanel.IsVisible = false;
            SpotlightCanvas.Children.Clear();
            DrawFullOverlay(step.BlockBackgroundClicks);
            CenterTextPanel();
            _subscribedStep = step;
            _advanceFiredThisStep = false;
            return;
        }

        TxtStepCounter.Text = string.Format(Loc.Get("window_tutorial_overlay_step_counter_fmt"), GetCurrentStepIndex() + 1, GetTotalSteps());
        TxtIcon.Text = step.Icon;
        TxtTitle.Text = step.Title;
        TxtDescription.Text = step.Description;

        BtnSupport.IsVisible = step.Id == "support";
        BtnSkip.IsVisible = !step.IsFollowUpCard;
        BtnSkipStep.IsVisible = step.AllowManualSkip && !step.IsFollowUpCard;

        bool isManual = step.AdvanceTrigger == TutorialAdvanceTrigger.Manual;
        BtnNext.IsVisible = isManual && !step.IsFollowUpCard;
        BtnNext.Content = IsLastStep() ? Loc.Get("window_tutorial_overlay_finish_content") : Loc.Get("btn_next");

        BtnPrevious.IsVisible = isManual && !IsFirstStep() && !step.IsFollowUpCard;

        if (step.IsFollowUpCard)
        {
            FollowUpPanel.IsVisible = true;
            ConfigureFollowUpButton(BtnFollowUp1, step.FollowUpButton1Text, step.FollowUpAction1);
            ConfigureFollowUpButton(BtnFollowUp2, step.FollowUpButton2Text, step.FollowUpAction2);
            ConfigureFollowUpButton(BtnFollowUp3, step.FollowUpButton3Text, step.FollowUpAction3);
        }
        else
        {
            FollowUpPanel.IsVisible = false;
        }

        try { UpdateSpotlight(step); } catch { }
        try { SubscribeAdvanceTrigger(step); } catch { }

        Dispatcher.UIThread.Post(() => { try { Focus(); } catch { } });
    }

    private static void ConfigureFollowUpButton(Button btn, string? text, Action<TutorialStep>? handler)
    {
        if (string.IsNullOrEmpty(text) || handler == null)
        {
            btn.IsVisible = false;
            return;
        }

        btn.Content = text;
        btn.IsVisible = true;
    }

    private void UpdateSpotlight(TutorialStep step)
    {
        try { _spotlightDelayTimer?.Stop(); } catch { }
        _spotlightDelayTimer = null;

        SpotlightCanvas.Children.Clear();

        bool clickThroughHole = step.AdvanceTrigger != TutorialAdvanceTrigger.Manual && !step.IsFollowUpCard;

        if (step.IsFollowUpCard ||
            string.IsNullOrEmpty(step.TargetElementName) ||
            step.TextPosition == TutorialStepPosition.Center)
        {
            DrawFullOverlay(step.BlockBackgroundClicks);
            CenterTextPanel();
            return;
        }

        try
        {
            step.PrepareTargetWindowAction?.Invoke(_targetWindow);
        }
        catch { }

        var targetElement = FindElementByName(_targetWindow, step.TargetElementName);
        if (targetElement == null)
        {
            DrawFullOverlay(step.BlockBackgroundClicks);
            CenterTextPanel();
            return;
        }

        if (targetElement is TextBox) clickThroughHole = true;

        var bounds = GetElementBounds(targetElement);

        if (bounds.X == 0 && bounds.Y == 0 && bounds.Width <= 100)
        {
            var currentStep = step;
            _spotlightDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _spotlightDelayTimer.Tick += (s, e) =>
            {
                try { _spotlightDelayTimer?.Stop(); } catch { }
                _spotlightDelayTimer = null;

                if (_thisOverlayCompleted) return;
                if (GetCurrentStep() == currentStep)
                {
                    var retryBounds = GetElementBounds(targetElement);
                    SpotlightCanvas.Children.Clear();
                    DrawSpotlightOverlay(retryBounds, clickThroughHole);
                    PositionTextPanel(retryBounds, currentStep.TextPosition);
                }
            };
            _spotlightDelayTimer.Start();

            DrawFullOverlay(step.BlockBackgroundClicks);
            CenterTextPanel();
        }
        else
        {
            DrawSpotlightOverlay(bounds, clickThroughHole);
            PositionTextPanel(bounds, step.TextPosition);
        }
    }

    private void DrawFullOverlay(bool blockClicks = true)
    {
        byte alpha = blockClicks ? (byte)0xA0 : (byte)0x00;

        var overlay = new Rectangle
        {
            Width = Bounds.Width > 0 ? Bounds.Width : Width,
            Height = Bounds.Height > 0 ? Bounds.Height : Height,
            Fill = new SolidColorBrush(Color.FromArgb(alpha, 0x00, 0x00, 0x00)),
            IsHitTestVisible = blockClicks
        };

        Canvas.SetLeft(overlay, 0);
        Canvas.SetTop(overlay, 0);
        SpotlightCanvas.Children.Add(overlay);
    }

    private void DrawSpotlightOverlay(Rect highlightBounds, bool clickThroughHole)
    {
        var padding = 8.0;
        var glowBounds = new Rect(
            highlightBounds.X - padding,
            highlightBounds.Y - padding,
            highlightBounds.Width + padding * 2,
            highlightBounds.Height + padding * 2);

        var w = Bounds.Width > 0 ? Bounds.Width : Width;
        var h = Bounds.Height > 0 ? Bounds.Height : Height;

        var fullRect = new RectangleGeometry(new Rect(0, 0, w, h));
        var spotlightRect = new RectangleGeometry(glowBounds, 8, 8);

        Geometry darkGeometry = clickThroughHole
            ? new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, spotlightRect)
            : fullRect;

        byte spotlightAlpha = 0xA0;
        var darkPath = new global::Avalonia.Controls.Shapes.Path
        {
            Data = darkGeometry,
            Fill = new SolidColorBrush(Color.FromArgb(spotlightAlpha, 0x00, 0x00, 0x00)),
            IsHitTestVisible = true
        };
        SpotlightCanvas.Children.Add(darkPath);

        var glowBorder = new Border
        {
            Width = glowBounds.Width,
            Height = glowBounds.Height,
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
        glowBorder.BoxShadow = new BoxShadows(new BoxShadow
        {
            Color = Color.FromArgb(0xB2, 0xFF, 0x69, 0xB4),
            Blur = 15,
            OffsetX = 0,
            OffsetY = 0,
            Spread = 0
        });

        Canvas.SetLeft(glowBorder, glowBounds.X);
        Canvas.SetTop(glowBorder, glowBounds.Y);
        SpotlightCanvas.Children.Add(glowBorder);
    }

    private void PositionTextPanel(Rect targetBounds, TutorialStepPosition position)
    {
        TextPanel.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
        TextPanel.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;

        var panelWidth = TextPanel.Bounds.Width > 0 ? TextPanel.Bounds.Width : 460;
        var panelHeight = TextPanel.Bounds.Height > 0 ? TextPanel.Bounds.Height : 220;

        const double margin = 20;

        var (left, top) = ComputePanelPosition(position, targetBounds, panelWidth, panelHeight, margin);

        var w = Bounds.Width > 0 ? Bounds.Width : Width;
        var h = Bounds.Height > 0 ? Bounds.Height : Height;

        double clampedLeft = Math.Max(margin, Math.Min(left, w - panelWidth - margin));
        double clampedTop = Math.Max(margin, Math.Min(top, h - panelHeight - margin));

        var panelRect = new Rect(clampedLeft, clampedTop, panelWidth, panelHeight);
        if (panelRect.Intersects(targetBounds))
        {
            var flipped = FlipPosition(position);
            var (altLeft, altTop) = ComputePanelPosition(flipped, targetBounds, panelWidth, panelHeight, margin);
            double altClampedLeft = Math.Max(margin, Math.Min(altLeft, w - panelWidth - margin));
            double altClampedTop = Math.Max(margin, Math.Min(altTop, h - panelHeight - margin));
            var altRect = new Rect(altClampedLeft, altClampedTop, panelWidth, panelHeight);
            if (!altRect.Intersects(targetBounds))
            {
                clampedLeft = altClampedLeft;
                clampedTop = altClampedTop;
            }
        }

        TextPanel.Margin = new Thickness(clampedLeft, clampedTop, 0, 0);
    }

    private static (double left, double top) ComputePanelPosition(
        TutorialStepPosition position, Rect targetBounds,
        double panelWidth, double panelHeight, double margin)
    {
        double left = 0, top = 0;
        switch (position)
        {
            case TutorialStepPosition.Bottom:
                left = targetBounds.X + (targetBounds.Width - panelWidth) / 2;
                top = targetBounds.Bottom + margin;
                break;
            case TutorialStepPosition.Top:
                left = targetBounds.X + (targetBounds.Width - panelWidth) / 2;
                top = targetBounds.Top - panelHeight - margin;
                break;
            case TutorialStepPosition.Left:
                left = targetBounds.Left - panelWidth - margin;
                top = targetBounds.Y + (targetBounds.Height - panelHeight) / 2;
                break;
            case TutorialStepPosition.Right:
                left = targetBounds.Right + margin;
                top = targetBounds.Y + (targetBounds.Height - panelHeight) / 2;
                break;
        }
        return (left, top);
    }

    private static TutorialStepPosition FlipPosition(TutorialStepPosition p) => p switch
    {
        TutorialStepPosition.Top => TutorialStepPosition.Bottom,
        TutorialStepPosition.Bottom => TutorialStepPosition.Top,
        TutorialStepPosition.Left => TutorialStepPosition.Right,
        TutorialStepPosition.Right => TutorialStepPosition.Left,
        _ => TutorialStepPosition.Bottom
    };

    private void CenterTextPanel()
    {
        TextPanel.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
        TextPanel.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
        TextPanel.Margin = new Thickness(0);
    }

    private Control? FindElementByName(Visual? parent, string name)
    {
        if (parent == null) return null;

        if (parent is Control control && control.Name == name)
        {
            return control;
        }

        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Control childControl && childControl.Name == name)
            {
                return childControl;
            }

            var result = FindElementByName(child, name);
            if (result != null) return result;
        }

        return null;
    }

    private Rect GetElementBounds(Visual element)
    {
        try
        {
            var topLeft = element.PointToScreen(new global::Avalonia.Point(0, 0));
            var bottomRight = element.PointToScreen(new global::Avalonia.Point(element.Bounds.Width, element.Bounds.Height));
            var localTopLeft = this.PointToClient(topLeft);
            var localBottomRight = this.PointToClient(bottomRight);
            return new Rect(localTopLeft, localBottomRight);
        }
        catch
        {
            return new Rect(0, 0, 100, 40);
        }
    }

    private static Window? FindWindowByTypeName(string typeName)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.Windows.FirstOrDefault(w => w.GetType().Name == typeName && w.IsLoaded);
            }
        }
        catch { }
        return null;
    }

    private void SubscribeAdvanceTrigger(TutorialStep step)
    {
        UnsubscribeAdvanceTrigger();
        _subscribedStep = step;
        _advanceFiredThisStep = false;

        if (step.AdvanceTrigger == TutorialAdvanceTrigger.Manual ||
            step.AdvanceTrigger == TutorialAdvanceTrigger.OnEvent)
        {
            return;
        }

        if (string.IsNullOrEmpty(step.TargetElementName)) return;
        var target = FindElementByName(_targetWindow, step.TargetElementName);
        if (target == null) return;

        switch (step.AdvanceTrigger)
        {
            case TutorialAdvanceTrigger.OnButtonClick:
                if (target is Button btn)
                {
                    _subButton = btn;
                    _subButtonHandler = (_, __) => AdvanceSync();
                    btn.Click += _subButtonHandler;
                }
                break;

            case TutorialAdvanceTrigger.OnTextEquals:
                if (target is TextBox tb)
                {
                    _subTextBox = tb;
                    _subTextHandler = (_, __) =>
                    {
                        if (_subscribedStep is { } s && _subTextBox != null &&
                            TextMatches(_subTextBox.Text ?? "", s.AdvanceValue ?? ""))
                        {
                            Advance();
                        }
                    };
                    tb.TextChanged += _subTextHandler;
                }
                break;

            case TutorialAdvanceTrigger.OnSelectionEquals:
                if (target is SelectingItemsControl sel)
                {
                    _subSelector = sel;
                    _subSelectionHandler = (_, __) =>
                    {
                        if (_subscribedStep is not { } s || _subSelector == null) return;

                        if (string.IsNullOrEmpty(s.AdvanceValue))
                        {
                            Advance();
                            return;
                        }

                        var actual = GetSelectorValue(_subSelector, s.MatchByTag);
                        if (!string.IsNullOrEmpty(actual) &&
                            string.Equals(actual, s.AdvanceValue, StringComparison.OrdinalIgnoreCase))
                        {
                            Advance();
                        }
                    };
                    sel.SelectionChanged += _subSelectionHandler;
                }
                break;

            case TutorialAdvanceTrigger.OnSliderAtLeast:
                if (target is Slider sl)
                {
                    _subSlider = sl;
                    _subSliderHandler = (_, __) =>
                    {
                        if (_subscribedStep is not { } s || _subSlider == null) return;

                        var v = _subSlider.Value;
                        if (v < s.AdvanceMinValue) return;
                        if (!double.IsNaN(s.AdvanceMaxValue) && v > s.AdvanceMaxValue) return;
                        Advance();
                    };
                    sl.ValueChanged += _subSliderHandler;
                }
                break;
        }
    }

    private void UnsubscribeAdvanceTrigger()
    {
        if (_subButton != null && _subButtonHandler != null)
        {
            try { _subButton.Click -= _subButtonHandler; } catch { }
            _subButton = null;
            _subButtonHandler = null;
        }

        if (_subTextBox != null && _subTextHandler != null)
        {
            try { _subTextBox.TextChanged -= _subTextHandler; } catch { }
            _subTextBox = null;
            _subTextHandler = null;
        }

        if (_subSelector != null && _subSelectionHandler != null)
        {
            try { _subSelector.SelectionChanged -= _subSelectionHandler; } catch { }
            _subSelector = null;
            _subSelectionHandler = null;
        }

        if (_subSlider != null && _subSliderHandler != null)
        {
            try { _subSlider.ValueChanged -= _subSliderHandler; } catch { }
            _subSlider = null;
            _subSliderHandler = null;
        }

        _subscribedStep = null;
    }

    private static string? GetSelectorValue(SelectingItemsControl sel, bool matchByTag)
    {
        var selected = sel.SelectedItem;
        if (selected is ContentControl cbi)
        {
            return matchByTag ? cbi.Tag?.ToString() : cbi.Content?.ToString();
        }
        return selected?.ToString();
    }

    private static bool TextMatches(string actual, string expected)
    {
        actual = (actual ?? "").Trim();
        expected = (expected ?? "").Trim();
        if (expected.Length == 0) return false;

        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;

        if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var av) &&
            double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var ev))
        {
            return Math.Abs(av - ev) < 0.5;
        }

        return actual.Length >= expected.Length &&
               actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void AdvanceSync()
    {
        if (_advanceFiredThisStep) return;
        _advanceFiredThisStep = true;
        UnsubscribeAdvanceTrigger();

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                dynamic? ts = _tutorialService;
                if (ts?.IsActive == true) ts.Next();
            }
            catch { }
        });
    }

    private void Advance()
    {
        AdvanceSync();
    }

    private void BtnNext_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            dynamic? ts = _tutorialService;
            if (ts?.IsActive == true) ts.Next();
        }
        catch { }
    }

    private void BtnPrevious_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            dynamic? ts = _tutorialService;
            if (ts?.IsActive == true) ts.Previous();
        }
        catch { }
    }

    private void BtnSkip_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            dynamic? ts = _tutorialService;
            ts?.Skip();
        }
        catch { }
    }

    private void BtnSkipStep_Click(object? sender, RoutedEventArgs e) => Advance();

    private void BtnFollowUp1_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            dynamic? ts = _tutorialService;
            var step = ts?.CurrentStep as TutorialStep;
            step?.FollowUpAction1?.Invoke(step);
        }
        catch { }
    }

    private void BtnFollowUp2_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            dynamic? ts = _tutorialService;
            var step = ts?.CurrentStep as TutorialStep;
            step?.FollowUpAction2?.Invoke(step);
        }
        catch { }
    }

    private void BtnFollowUp3_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            dynamic? ts = _tutorialService;
            var step = ts?.CurrentStep as TutorialStep;
            step?.FollowUpAction3?.Invoke(step);
        }
        catch { }
    }

    private void BtnSupport_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://linktr.ee/CodeBambi",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void DetachAllSubscriptions()
    {
        _thisOverlayCompleted = true;
        try { _spotlightDelayTimer?.Stop(); } catch { }
        _spotlightDelayTimer = null;
        UnsubscribeAdvanceTrigger();
        DetachFromTarget(_targetWindow);
    }

    private async Task RunFadeAnimation(double from, double to, TimeSpan duration)
    {
        var animation = new Animation
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
            await RunFadeAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            Close();
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachAllSubscriptions();
        base.OnClosed(e);
    }

    private int GetCurrentStepIndex()
    {
        try
        {
            dynamic? ts = _tutorialService;
            return ts?.CurrentStepIndex ?? 0;
        }
        catch { return 0; }
    }

    private int GetTotalSteps()
    {
        try
        {
            dynamic? ts = _tutorialService;
            return ts?.TotalSteps ?? 0;
        }
        catch { return 0; }
    }

    private TutorialStep? GetCurrentStep()
    {
        try
        {
            dynamic? ts = _tutorialService;
            return ts?.CurrentStep as TutorialStep;
        }
        catch { return null; }
    }

    private bool IsFirstStep()
    {
        try
        {
            dynamic? ts = _tutorialService;
            return ts?.IsFirstStep ?? false;
        }
        catch { return false; }
    }

    private bool IsLastStep()
    {
        try
        {
            dynamic? ts = _tutorialService;
            return ts?.IsLastStep ?? false;
        }
        catch { return false; }
    }
}
