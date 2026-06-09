using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel;

/// <summary>
/// Draws one line of text with a crisp outline (stroke) under a solid fill — the readable
/// "subtitle" look the Chaos announcer needs over any live-desktop background. Uses
/// <see cref="FormattedText.BuildGeometry"/> so the border is a true outline, not a drop
/// shadow. Call <see cref="Build"/> once the element is in the visual tree (it sizes itself).
/// </summary>
public sealed class OutlinedText : FrameworkElement
{
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 60;
    public Brush Fill { get; set; } = Brushes.White;
    public Brush Stroke { get; set; } = Brushes.Black;
    public double StrokeThickness { get; set; } = 3.2;
    public FontWeight FontWeight { get; set; } = FontWeights.Bold;
    public FontFamily Family { get; set; } = new("Segoe UI");

    private Geometry? _geo;
    private double _pad;

    public void Build()
    {
        if (string.IsNullOrEmpty(Text)) { _geo = null; Width = Height = 0; InvalidateVisual(); return; }

        double dpi = 1.0;
        try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }

        var ft = new FormattedText(
            Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Family, FontStyles.Normal, FontWeight, FontStretches.Normal),
            FontSize, Fill, dpi);

        _pad = StrokeThickness + 6;
        _geo = ft.BuildGeometry(new Point(_pad, _pad));
        if (_geo != null && _geo.CanFreeze) _geo.Freeze();
        Width = ft.WidthIncludingTrailingWhitespace + _pad * 2;
        Height = ft.Height + _pad * 2;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_geo == null) return;
        // Stroke pass first (doubled so the half clipped by the fill still reads), then the fill.
        var pen = new Pen(Stroke, StrokeThickness * 2) { LineJoin = PenLineJoin.Round };
        if (pen.CanFreeze) pen.Freeze();
        dc.DrawGeometry(null, pen, _geo);
        dc.DrawGeometry(Fill, null, _geo);
    }
}
