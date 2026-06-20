using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Inverts a boolean, collection count, or null state and converts it to a
/// <see cref="Avalonia.Controls.Visual.IsVisible"/> value.
/// </summary>
public sealed class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;

        if (value is int i)
            return i == 0;

        if (value is ICollection collection)
            return collection.Count == 0;

        return value is null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }
}
