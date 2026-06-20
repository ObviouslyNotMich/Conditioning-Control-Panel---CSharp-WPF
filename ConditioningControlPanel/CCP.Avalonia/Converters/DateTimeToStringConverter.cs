using System.Globalization;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Formats a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> using the
/// invariant culture so dates render consistently regardless of UI locale.
/// </summary>
public sealed class DateTimeToStringConverter : IValueConverter
{
    public string Format { get; set; } = "g";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return dt.ToString(Format, CultureInfo.InvariantCulture);

        if (value is DateTimeOffset dto)
            return dto.ToString(Format, CultureInfo.InvariantCulture);

        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
