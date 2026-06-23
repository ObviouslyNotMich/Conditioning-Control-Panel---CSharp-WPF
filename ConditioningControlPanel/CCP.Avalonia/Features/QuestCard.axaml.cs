using System;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Reusable daily/weekly quest card. Shows an image, title, description,
/// progress bar, XP badge, reroll button, and a completion checkmark overlay.
/// </summary>
public partial class QuestCard : UserControl
{
    public static readonly DirectProperty<QuestCard, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, string>(
            nameof(Title), o => o.Title, (o, v) => o.Title = v);

    public static readonly DirectProperty<QuestCard, string> DescriptionProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, string>(
            nameof(Description), o => o.Description, (o, v) => o.Description = v);

    public static readonly DirectProperty<QuestCard, string?> GlyphProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, string?>(
            nameof(Glyph), o => o.Glyph, (o, v) => o.Glyph = v);

    public static readonly DirectProperty<QuestCard, string?> ImageUriProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, string?>(
            nameof(ImageUri), o => o.ImageUri, (o, v) => o.ImageUri = v);

    public static readonly DirectProperty<QuestCard, double> ProgressFractionProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, double>(
            nameof(ProgressFraction), o => o.ProgressFraction, (o, v) => o.ProgressFraction = v);

    public static readonly DirectProperty<QuestCard, string> ProgressTextProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, string>(
            nameof(ProgressText), o => o.ProgressText, (o, v) => o.ProgressText = v);

    public static readonly DirectProperty<QuestCard, string> XpTextProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, string>(
            nameof(XpText), o => o.XpText, (o, v) => o.XpText = v);

    public static readonly DirectProperty<QuestCard, bool> IsCompletedProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, bool>(
            nameof(IsCompleted), o => o.IsCompleted, (o, v) => o.IsCompleted = v);

    public static readonly DirectProperty<QuestCard, string> RerollTextProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, string>(
            nameof(RerollText), o => o.RerollText, (o, v) => o.RerollText = v);

    public static readonly DirectProperty<QuestCard, ICommand?> RerollCommandProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, ICommand?>(
            nameof(RerollCommand), o => o.RerollCommand, (o, v) => o.RerollCommand = v);

    public static readonly DirectProperty<QuestCard, object?> CommandParameterProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, object?>(
            nameof(CommandParameter), o => o.CommandParameter, (o, v) => o.CommandParameter = v);

    public static readonly DirectProperty<QuestCard, bool> IsRerollVisibleProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, bool>(
            nameof(IsRerollVisible), o => o.IsRerollVisible, (o, v) => o.IsRerollVisible = v);

    public static readonly DirectProperty<QuestCard, bool> RerollEnabledProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, bool>(
            nameof(RerollEnabled), o => o.RerollEnabled, (o, v) => o.RerollEnabled = v);

    public static readonly DirectProperty<QuestCard, IBrush?> BorderBrushOverrideProperty =
        AvaloniaProperty.RegisterDirect<QuestCard, IBrush?>(
            nameof(BorderBrushOverride), o => o.BorderBrushOverride, (o, v) => o.BorderBrushOverride = v);

    private string _title = "Quest";
    private string _description = "";
    private string? _glyph;
    private string? _imageUri;
    private double _progressFraction;
    private string _progressText = "0 / 0";
    private string _xpText = "🎁 0 XP";
    private bool _isCompleted;
    private string _rerollText = "🔄 Reroll";
    private ICommand? _rerollCommand;
    private object? _commandParameter;
    private bool _isRerollVisible = true;
    private bool _rerollEnabled = true;
    private IBrush? _borderBrushOverride;

    public string Title
    {
        get => _title;
        set => SetAndRaise(TitleProperty, ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => SetAndRaise(DescriptionProperty, ref _description, value);
    }

    public string? Glyph
    {
        get => _glyph;
        set => SetAndRaise(GlyphProperty, ref _glyph, value);
    }

    public string? ImageUri
    {
        get => _imageUri;
        set => SetAndRaise(ImageUriProperty, ref _imageUri, value);
    }

    public double ProgressFraction
    {
        get => _progressFraction;
        set => SetAndRaise(ProgressFractionProperty, ref _progressFraction, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetAndRaise(ProgressTextProperty, ref _progressText, value);
    }

    public string XpText
    {
        get => _xpText;
        set => SetAndRaise(XpTextProperty, ref _xpText, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetAndRaise(IsCompletedProperty, ref _isCompleted, value);
    }

    public string RerollText
    {
        get => _rerollText;
        set => SetAndRaise(RerollTextProperty, ref _rerollText, value);
    }

    public ICommand? RerollCommand
    {
        get => _rerollCommand;
        set => SetAndRaise(RerollCommandProperty, ref _rerollCommand, value);
    }

    public object? CommandParameter
    {
        get => _commandParameter;
        set => SetAndRaise(CommandParameterProperty, ref _commandParameter, value);
    }

    public bool IsRerollVisible
    {
        get => _isRerollVisible;
        set => SetAndRaise(IsRerollVisibleProperty, ref _isRerollVisible, value);
    }

    public bool RerollEnabled
    {
        get => _rerollEnabled;
        set => SetAndRaise(RerollEnabledProperty, ref _rerollEnabled, value);
    }

    public IBrush? BorderBrushOverride
    {
        get => _borderBrushOverride;
        set => SetAndRaise(BorderBrushOverrideProperty, ref _borderBrushOverride, value);
    }

    static QuestCard()
    {
        TitleProperty.Changed.AddClassHandler<QuestCard>(OnTitleChanged);
        DescriptionProperty.Changed.AddClassHandler<QuestCard>(OnDescriptionChanged);
        GlyphProperty.Changed.AddClassHandler<QuestCard>(OnGlyphChanged);
        ImageUriProperty.Changed.AddClassHandler<QuestCard>(OnImageUriChanged);
        ProgressFractionProperty.Changed.AddClassHandler<QuestCard>(OnProgressFractionChanged);
        ProgressTextProperty.Changed.AddClassHandler<QuestCard>(OnProgressTextChanged);
        XpTextProperty.Changed.AddClassHandler<QuestCard>(OnXpTextChanged);
        IsCompletedProperty.Changed.AddClassHandler<QuestCard>(OnIsCompletedChanged);
        RerollTextProperty.Changed.AddClassHandler<QuestCard>(OnRerollTextChanged);
        RerollCommandProperty.Changed.AddClassHandler<QuestCard>(OnRerollCommandChanged);
        CommandParameterProperty.Changed.AddClassHandler<QuestCard>(OnCommandParameterChanged);
        IsRerollVisibleProperty.Changed.AddClassHandler<QuestCard>(OnIsRerollVisibleChanged);
        RerollEnabledProperty.Changed.AddClassHandler<QuestCard>(OnRerollEnabledChanged);
        BorderBrushOverrideProperty.Changed.AddClassHandler<QuestCard>(OnBorderBrushOverrideChanged);
    }

    public QuestCard()
    {
        InitializeComponent();
    }

    private static void OnTitleChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtTitle.Text = c.Title ?? "";
    }

    private static void OnDescriptionChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtDescription.Text = c.Description ?? "";
    }

    private static void OnGlyphChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtGlyph.Text = c.Glyph ?? "";
    }

    private static void OnImageUriChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ImgQuest.Source = LoadBitmapFromUri(c.ImageUri);
    }

    private static void OnProgressFractionChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ProgressBar.Value = Math.Max(0.0, Math.Min(1.0, c.ProgressFraction));
    }

    private static void OnProgressTextChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtProgress.Text = c.ProgressText ?? "";
    }

    private static void OnXpTextChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtXp.Text = c.XpText ?? "";
    }

    private static void OnIsCompletedChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.CompletedOverlay.IsVisible = c.IsCompleted;
    }

    private static void OnRerollTextChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnReroll.Content = c.RerollText ?? "🔄 Reroll";
    }

    private static void OnRerollCommandChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnReroll.Command = c.RerollCommand;
    }

    private static void OnCommandParameterChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnReroll.CommandParameter = c.CommandParameter;
    }

    private static void OnIsRerollVisibleChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnReroll.IsVisible = c.IsRerollVisible;
    }

    private static void OnRerollEnabledChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnReroll.IsEnabled = c.RerollEnabled;
    }

    private static void OnBorderBrushOverrideChanged(QuestCard c, AvaloniaPropertyChangedEventArgs e)
    {
        if (c.BorderBrushOverride is not null)
            c.RootBorder.BorderBrush = c.BorderBrushOverride;
    }

    private static Bitmap? LoadBitmapFromUri(string? uri)
    {
        return AvaloniaBitmapHelper.Load(uri);
    }
}
