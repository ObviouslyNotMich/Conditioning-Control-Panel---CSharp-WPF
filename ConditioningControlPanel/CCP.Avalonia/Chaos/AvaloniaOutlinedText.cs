using System.Globalization;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of WPF OutlinedText: draws one line of text with a crisp outline
/// (stroke) under a solid fill. Uses FormattedText.BuildGeometry.
/// Call Build() once the text properties are set.
/// </summary>
public sealed class AvaloniaOutlinedText : Control
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<AvaloniaOutlinedText, string>(nameof(Text), "");
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<AvaloniaOutlinedText, double>(nameof(FontSize), 60);
    public static readonly StyledProperty<IBrush> FillProperty =
        AvaloniaProperty.Register<AvaloniaOutlinedText, IBrush>(nameof(Fill), Brushes.White);
    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<AvaloniaOutlinedText, IBrush>(nameof(Stroke), Brushes.Black);
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<AvaloniaOutlinedText, double>(nameof(StrokeThickness), 3.2);
    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<AvaloniaOutlinedText, FontWeight>(nameof(FontWeight), FontWeight.Bold);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }
    public IBrush Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }
    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    private Geometry? _geo;
    private double _pad;

    public void Build()
    {
        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            _geo = null;
            Width = Height = 0;
            InvalidateVisual();
            return;
        }

        _pad = StrokeThickness + 6;
        var ft = new FormattedText(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight),
            FontSize, Fill);

        _geo = ft.BuildGeometry(new Point(_pad, _pad));
        Width = ft.WidthIncludingTrailingWhitespace + _pad * 2;
        Height = ft.Height + _pad * 2;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontSizeProperty
            || change.Property == FillProperty || change.Property == StrokeProperty
            || change.Property == StrokeThicknessProperty || change.Property == FontWeightProperty)
        {
            Build();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_geo == null) return;
        var pen = new Pen(Stroke, StrokeThickness * 2) { LineJoin = PenLineJoin.Round };
        context.DrawGeometry(null, pen, _geo);
        context.DrawGeometry(Fill, null, _geo);
    }
}
