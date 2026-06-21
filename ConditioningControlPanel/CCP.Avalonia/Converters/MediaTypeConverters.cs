using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Maps a Deeper media-type string to a display glyph.
/// </summary>
public sealed class MediaTypeToGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value?.ToString()?.ToLowerInvariant()) switch
        {
            "video" => "🎬",
            "audio" => "🎧",
            "haptics" => "📳",
            "webcam" => "📹",
            _ => "📁"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a Deeper media-type string to an accent brush.
/// </summary>
public sealed class MediaTypeToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = (value?.ToString()?.ToLowerInvariant()) switch
        {
            "video" => Color.Parse("#E84A6B"),
            "audio" => Color.Parse("#5865F2"),
            "haptics" => Color.Parse("#FF69B4"),
            "webcam" => Color.Parse("#00D4AA"),
            _ => Color.Parse("#FFAAAAAA")
        };
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
