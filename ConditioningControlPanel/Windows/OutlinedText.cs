using System.Collections.Concurrent;
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

    // FormattedText.BuildGeometry() (CPU glyph→outline rasterization) runs on the UI thread on every
    // Build(); the Chaos announcer/pop-text floats one constantly during a run (score/gold/effect labels),
    // so re-rasterizing the same strings each spawn is a real per-spawn hitch. Cache the FROZEN outline
    // geometry (brush-independent — Fill/Stroke are applied in OnRender, not baked into the geometry) keyed
    // by the inputs that change the outline. Frozen ⇒ safe to share across instances/threads.
    private readonly record struct GeoEntry(Geometry? Geo, double Pad, double Width, double Height);
    private static readonly ConcurrentDictionary<string, GeoEntry> s_geoCache = new();
    private const int GeoCacheCap = 512;   // bounded: distinct labels per run are few; clear if it ever runs away

    public void Build()
    {
        if (string.IsNullOrEmpty(Text)) { _geo = null; Width = Height = 0; InvalidateVisual(); return; }

        double dpi = 1.0;
        try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }

        var key = string.Concat(Text, "\x1f", FontSize.ToString(CultureInfo.InvariantCulture), "\x1f",
            FontWeight.ToOpenTypeWeight().ToString(), "\x1f", Family.Source, "\x1f",
            StrokeThickness.ToString(CultureInfo.InvariantCulture), "\x1f", dpi.ToString("F2", CultureInfo.InvariantCulture));

        if (s_geoCache.TryGetValue(key, out var hit))
        {
            _geo = hit.Geo; _pad = hit.Pad; Width = hit.Width; Height = hit.Height;
            InvalidateVisual();
            return;
        }

        var ft = new FormattedText(
            Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Family, FontStyles.Normal, FontWeight, FontStretches.Normal),
            FontSize, Fill, dpi);

        _pad = StrokeThickness + 6;
        _geo = ft.BuildGeometry(new Point(_pad, _pad));
        if (_geo != null && _geo.CanFreeze) _geo.Freeze();
        Width = ft.WidthIncludingTrailingWhitespace + _pad * 2;
        Height = ft.Height + _pad * 2;

        if (s_geoCache.Count >= GeoCacheCap) s_geoCache.Clear();
        s_geoCache[key] = new GeoEntry(_geo, _pad, Width, Height);
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
