using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Converts a boolean to one of two configurable brushes.
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public IBrush TrueBrush { get; set; } =
        global::Avalonia.Application.Current?.TryGetResource("TextLightBrush", global::Avalonia.Styling.ThemeVariant.Default, out var light) == true && light is IBrush lb
            ? lb
            : new SolidColorBrush(Colors.White);

    public IBrush FalseBrush { get; set; } =
        global::Avalonia.Application.Current?.TryGetResource("TextDimBrush", global::Avalonia.Styling.ThemeVariant.Default, out var dim) == true && dim is IBrush db
            ? db
            : new SolidColorBrush(Colors.Gray);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TrueBrush : FalseBrush;

        return value is not null ? TrueBrush : FalseBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
