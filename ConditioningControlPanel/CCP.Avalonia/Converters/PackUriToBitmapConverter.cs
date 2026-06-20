using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Converts a pack:// or file:// URI string into an Avalonia <see cref="Bitmap"/>.
/// Used by the Season Recap card badge grid and other ported WPF surfaces that
/// bind Image.Source to legacy resource URIs.
/// </summary>
public sealed class PackUriToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return null;

        try
        {
            // Direct file path.
            if (File.Exists(s))
                return new Bitmap(s);

            // file:// URI.
            if (s.StartsWith("file://", StringComparison.Ordinal))
            {
                var path = s.Substring(7);
                if (File.Exists(path))
                    return new Bitmap(path);
                return null;
            }

            // pack://application:,,,/Resources/... -> avares://CCP.Avalonia/Assets/...
            if (s.StartsWith("pack://application:,,,/", StringComparison.Ordinal))
            {
                var relative = s.Substring("pack://application:,,,/".Length);
                var avares = $"avares://CCP.Avalonia/Assets/{relative}";
                using var stream = AssetLoader.Open(new Uri(avares));
                return new Bitmap(stream);
            }

            // avares:// URI.
            if (s.StartsWith("avares://", StringComparison.Ordinal))
            {
                using var stream = AssetLoader.Open(new Uri(s));
                return new Bitmap(stream);
            }
        }
        catch
        {
            // Fail-soft: missing assets are expected until mod/image resources are ported.
        }

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
