using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Converts the dashboard browser site toggle (true = BambiCloud, false = HypnoTube)
/// into the site key passed to <see cref="SettingsTabViewModel.OpenBrowserCommand"/>.
/// </summary>
public sealed class BoolToBrowserSiteConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "bambicloud" : "hypnotube";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
