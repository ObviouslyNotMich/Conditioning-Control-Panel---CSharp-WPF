using System;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Roadmap node card showing a step image, title, requirement, status button,
/// and a lock overlay for steps that are not yet active.
/// </summary>
public partial class RoadmapNodeCard : UserControl
{
    public static readonly DirectProperty<RoadmapNodeCard, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, string>(
            nameof(Title), o => o.Title, (o, v) => o.Title = v);

    public static readonly DirectProperty<RoadmapNodeCard, string> RequirementProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, string>(
            nameof(Requirement), o => o.Requirement, (o, v) => o.Requirement = v);

    public static readonly DirectProperty<RoadmapNodeCard, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, string>(
            nameof(StatusText), o => o.StatusText, (o, v) => o.StatusText = v);

    public static readonly DirectProperty<RoadmapNodeCard, string?> IconUriProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, string?>(
            nameof(IconUri), o => o.IconUri, (o, v) => o.IconUri = v);

    public static readonly DirectProperty<RoadmapNodeCard, string?> GlyphProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, string?>(
            nameof(Glyph), o => o.Glyph, (o, v) => o.Glyph = v);

    public static readonly DirectProperty<RoadmapNodeCard, bool> IsLockedProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, bool>(
            nameof(IsLocked), o => o.IsLocked, (o, v) => o.IsLocked = v);

    public static readonly DirectProperty<RoadmapNodeCard, string?> AccentColorProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, string?>(
            nameof(AccentColor), o => o.AccentColor, (o, v) => o.AccentColor = v);

    public static readonly DirectProperty<RoadmapNodeCard, ICommand?> CommandProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, ICommand?>(
            nameof(Command), o => o.Command, (o, v) => o.Command = v);

    public static readonly DirectProperty<RoadmapNodeCard, object?> CommandParameterProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, object?>(
            nameof(CommandParameter), o => o.CommandParameter, (o, v) => o.CommandParameter = v);

    public static readonly DirectProperty<RoadmapNodeCard, string> StepNumberProperty =
        AvaloniaProperty.RegisterDirect<RoadmapNodeCard, string>(
            nameof(StepNumber), o => o.StepNumber, (o, v) => o.StepNumber = v);

    private string _title = LocalizationManager.Instance["label_title"];
    private string _requirement = LocalizationManager.Instance["roadmap_node_requirement_default"];
    private string _statusText = LocalizationManager.Instance["btn_start"];
    private string? _iconUri;
    private string? _glyph;
    private bool _isLocked;
    private string? _accentColor = "#FF69B4";
    private ICommand? _command;
    private object? _commandParameter;
    private string _stepNumber = LocalizationManager.Instance["roadmap_node_step_default"];

    public string Title
    {
        get => _title;
        set => SetAndRaise(TitleProperty, ref _title, value);
    }

    public string Requirement
    {
        get => _requirement;
        set => SetAndRaise(RequirementProperty, ref _requirement, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetAndRaise(StatusTextProperty, ref _statusText, value);
    }

    public string? IconUri
    {
        get => _iconUri;
        set => SetAndRaise(IconUriProperty, ref _iconUri, value);
    }

    public string? Glyph
    {
        get => _glyph;
        set => SetAndRaise(GlyphProperty, ref _glyph, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetAndRaise(IsLockedProperty, ref _isLocked, value);
    }

    public string? AccentColor
    {
        get => _accentColor;
        set => SetAndRaise(AccentColorProperty, ref _accentColor, value);
    }

    public ICommand? Command
    {
        get => _command;
        set => SetAndRaise(CommandProperty, ref _command, value);
    }

    public object? CommandParameter
    {
        get => _commandParameter;
        set => SetAndRaise(CommandParameterProperty, ref _commandParameter, value);
    }

    public string StepNumber
    {
        get => _stepNumber;
        set => SetAndRaise(StepNumberProperty, ref _stepNumber, value);
    }

    static RoadmapNodeCard()
    {
        TitleProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnTitleChanged);
        RequirementProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnRequirementChanged);
        StatusTextProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnStatusTextChanged);
        IconUriProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnIconUriChanged);
        GlyphProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnGlyphChanged);
        IsLockedProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnIsLockedChanged);
        AccentColorProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnAccentColorChanged);
        CommandProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnCommandChanged);
        CommandParameterProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnCommandParameterChanged);
        StepNumberProperty.Changed.AddClassHandler<RoadmapNodeCard>(OnStepNumberChanged);
    }

    public RoadmapNodeCard()
    {
        InitializeComponent();
    }

    private static void OnTitleChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtTitle.Text = c.Title ?? "";
    }

    private static void OnRequirementChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtRequirement.Text = c.Requirement ?? "";
    }

    private static void OnStatusTextChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnStatus.Content = c.StatusText ?? "";
    }

    private static void OnIconUriChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        var bitmap = LoadBitmapFromUri(c.IconUri);
        if (bitmap is not null)
        {
            c.ImgIconHost.Background = new ImageBrush(bitmap)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentY = AlignmentY.Center
            };
            c.ImgIconHost.IsVisible = true;
            c.TxtGlyph.IsVisible = false;
        }
        else if (!string.IsNullOrEmpty(c.Glyph))
        {
            c.ImgIconHost.IsVisible = false;
            c.TxtGlyph.IsVisible = true;
        }
        else
        {
            c.ImgIconHost.Background = null;
            c.ImgIconHost.IsVisible = true;
            c.TxtGlyph.IsVisible = false;
        }
    }

    private static void OnGlyphChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtGlyph.Text = c.Glyph ?? "";
        if (c.ImgIconHost.Background is null && !string.IsNullOrEmpty(c.Glyph))
        {
            c.ImgIconHost.IsVisible = false;
            c.TxtGlyph.IsVisible = true;
        }
    }

    private static void OnIsLockedChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.LockedOverlay.IsVisible = c.IsLocked;
        c.BtnStatus.IsEnabled = !c.IsLocked;
    }

    private static void OnAccentColorChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        if (c.AccentColor is not null && Color.TryParse(c.AccentColor, out var color))
        {
            var brush = new SolidColorBrush(color);
            c.RootBorder.BorderBrush = brush;
            c.TxtStepNumber.Foreground = brush;
            c.TxtGlyph.Foreground = brush;
        }
    }

    private static void OnCommandChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnStatus.Command = c.Command;
    }

    private static void OnCommandParameterChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnStatus.CommandParameter = c.CommandParameter;
    }

    private static void OnStepNumberChanged(RoadmapNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtStepNumber.Text = c.StepNumber ?? "";
    }

    private static Bitmap? LoadBitmapFromUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        try
        {
            if (File.Exists(uri))
                return new Bitmap(uri);

            if (uri.StartsWith("file://", StringComparison.Ordinal))
            {
                var path = uri.Substring(7);
                if (File.Exists(path))
                    return new Bitmap(path);
                return null;
            }

            if (uri.StartsWith("pack://application:,,,", StringComparison.Ordinal))
            {
                var relative = uri.Substring("pack://application:,,,".Length).TrimStart('/');
                if (relative.StartsWith("Resources/", StringComparison.Ordinal))
                    relative = relative.Substring("Resources/".Length);
                var avares = $"avares://CCP.Avalonia/Assets/{relative}";
                using var stream = AssetLoader.Open(new Uri(avares));
                return new Bitmap(stream);
            }

            if (uri.StartsWith("avares://", StringComparison.Ordinal))
            {
                using var stream = AssetLoader.Open(new Uri(uri));
                return new Bitmap(stream);
            }
        }
        catch
        {
            // Fail-soft: missing assets are expected until mod/image resources are ported.
        }

        return null;
    }
}
