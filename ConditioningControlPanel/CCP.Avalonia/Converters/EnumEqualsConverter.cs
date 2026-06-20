using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Compares a bound enum value with a converter parameter string and returns true when equal.
/// Supports two-way conversion so it can drive a group of RadioButtons bound to a single enum.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        var valueString = value.ToString();
        var paramString = parameter.ToString();
        return string.Equals(valueString, paramString, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            // targetType is the enum type (e.g. CornerPosition)
            if (targetType.IsEnum)
                return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);
        }

        return global::Avalonia.Data.BindingOperations.DoNothing;
    }
}
