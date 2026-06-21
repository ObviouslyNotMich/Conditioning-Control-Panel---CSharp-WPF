using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Maps a leaderboard rank to a medal glyph for the top three, otherwise a plain rank string.
/// </summary>
public sealed class RankToGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int rank)
        {
            return rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"#{rank}"
            };
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
