using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Compact click-to-select preset tile. Shows the preset name, a DEFAULT/CUSTOM
/// badge, and a row of compact feature-stat glyphs.
/// </summary>
public partial class PresetCard : UserControl
{
    private string _title = "Preset";
    public static readonly DirectProperty<PresetCard, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<PresetCard, string>(
            nameof(Title), o => o.Title, (o, v) => o.Title = v);

    private bool _isDefault;
    public static readonly DirectProperty<PresetCard, bool> IsDefaultProperty =
        AvaloniaProperty.RegisterDirect<PresetCard, bool>(
            nameof(IsDefault), o => o.IsDefault, (o, v) => o.IsDefault = v);

    private bool _isCustom;
    public static readonly DirectProperty<PresetCard, bool> IsCustomProperty =
        AvaloniaProperty.RegisterDirect<PresetCard, bool>(
            nameof(IsCustom), o => o.IsCustom, (o, v) => o.IsCustom = v);

    private bool _isSelected;
    public static readonly DirectProperty<PresetCard, bool> IsSelectedProperty =
        AvaloniaProperty.RegisterDirect<PresetCard, bool>(
            nameof(IsSelected), o => o.IsSelected, (o, v) => o.IsSelected = v);

    private IReadOnlyList<string> _featureStats = Array.Empty<string>();
    public static readonly DirectProperty<PresetCard, IReadOnlyList<string>> FeatureStatsProperty =
        AvaloniaProperty.RegisterDirect<PresetCard, IReadOnlyList<string>>(
            nameof(FeatureStats), o => o.FeatureStats, (o, v) => o.FeatureStats = v);

    private ICommand? _clickCommand;
    public static readonly DirectProperty<PresetCard, ICommand?> ClickCommandProperty =
        AvaloniaProperty.RegisterDirect<PresetCard, ICommand?>(
            nameof(ClickCommand), o => o.ClickCommand, (o, v) => o.ClickCommand = v);

    private object? _commandParameter;
    public static readonly DirectProperty<PresetCard, object?> CommandParameterProperty =
        AvaloniaProperty.RegisterDirect<PresetCard, object?>(
            nameof(CommandParameter), o => o.CommandParameter, (o, v) => o.CommandParameter = v);

    public static readonly RoutedEvent<RoutedEventArgs> ClickEvent =
        RoutedEvent.Register<PresetCard, RoutedEventArgs>(nameof(Click), RoutingStrategies.Bubble);

    public string Title
    {
        get => _title;
        set => SetAndRaise(TitleProperty, ref _title, value);
    }

    public bool IsDefault
    {
        get => _isDefault;
        set => SetAndRaise(IsDefaultProperty, ref _isDefault, value);
    }

    public bool IsCustom
    {
        get => _isCustom;
        set => SetAndRaise(IsCustomProperty, ref _isCustom, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetAndRaise(IsSelectedProperty, ref _isSelected, value);
    }

    public IReadOnlyList<string> FeatureStats
    {
        get => _featureStats;
        set => SetAndRaise(FeatureStatsProperty, ref _featureStats, value ?? Array.Empty<string>());
    }

    public ICommand? ClickCommand
    {
        get => _clickCommand;
        set => SetAndRaise(ClickCommandProperty, ref _clickCommand, value);
    }

    public object? CommandParameter
    {
        get => _commandParameter;
        set => SetAndRaise(CommandParameterProperty, ref _commandParameter, value);
    }

    public event EventHandler<RoutedEventArgs> Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    static PresetCard()
    {
        TitleProperty.Changed.AddClassHandler<PresetCard>(OnTitleChanged);
        IsDefaultProperty.Changed.AddClassHandler<PresetCard>(OnBadgeStateChanged);
        IsCustomProperty.Changed.AddClassHandler<PresetCard>(OnBadgeStateChanged);
        IsSelectedProperty.Changed.AddClassHandler<PresetCard>(OnIsSelectedChanged);
        FeatureStatsProperty.Changed.AddClassHandler<PresetCard>(OnFeatureStatsChanged);
    }

    public PresetCard()
    {
        InitializeComponent();
        RootBorder.PointerPressed += OnPointerPressed;
        ApplyBadgeState();
        ApplySelectionState();
    }

    private static void OnTitleChanged(PresetCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtTitle.Text = c.Title ?? "";
    }

    private static void OnBadgeStateChanged(PresetCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ApplyBadgeState();
    }

    private static void OnIsSelectedChanged(PresetCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ApplySelectionState();
    }

    private static void OnFeatureStatsChanged(PresetCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.StatsItems.ItemsSource = c.FeatureStats;
    }

    private void ApplyBadgeState()
    {
        if (IsDefault)
        {
            TxtBadge.Text = Loc.Get("preset_badge_default");
            BadgeBorder.Background = (IBrush?)Application.Current?.Resources["PanelAccentBrush"];
            BadgeBorder.IsVisible = true;
        }
        else if (IsCustom)
        {
            TxtBadge.Text = Loc.Get("preset_badge_custom");
            BadgeBorder.Background = (IBrush?)Application.Current?.Resources["PinkBrush"];
            BadgeBorder.IsVisible = true;
        }
        else
        {
            BadgeBorder.IsVisible = false;
        }
    }

    private void ApplySelectionState()
    {
        SelectionBorder.IsVisible = IsSelected;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Ignore presses that originate from a nested interactive child (none currently,
        // but this keeps the contract consistent with FeatureCard).
        if (e.Source is Visual src && !ReferenceEquals(src, this) && src.GetVisualAncestors().Contains(this) && src is Control { IsHitTestVisible: true })
        {
            // Allow inner controls to handle their own input.
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            e.Handled = true;

            var command = ClickCommand;
            var parameter = CommandParameter;
            if (command != null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }

            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        }
    }
}
