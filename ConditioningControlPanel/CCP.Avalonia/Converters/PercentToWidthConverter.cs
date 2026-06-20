using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Converts a percentage (0-100) into a width value by multiplying it against
/// a configurable maximum width supplied as the converter parameter.
/// </summary>
public sealed class PercentToWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!double.TryParse(value?.ToString(), NumberStyles.Any, culture, out var percent))
            return 0.0;

        var maxWidth = 200.0;
        if (parameter is not null && !double.TryParse(parameter.ToString(), NumberStyles.Any, culture, out maxWidth))
        {
            maxWidth = 200.0;
        }

        return Math.Max(0.0, maxWidth * percent / 100.0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
