using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Compares the bound value with the converter parameter as strings and returns a boolean.
/// Used to highlight header tab buttons when their key matches the currently selected tab.
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
