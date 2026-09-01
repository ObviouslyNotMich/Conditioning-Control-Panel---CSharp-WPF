using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/CompanionConverters.cs —
    /// the ONE converter of that file's eleven that survives the crossing.
    ///
    /// <para>Every <c>CompanionBoolToVisibilityConverter</c> usage becomes
    /// <c>IsVisible="{Binding X}"</c> / <c>IsVisible="{Binding !X}"</c>, and
    /// <c>EmojiToImageSourceConverter</c> is not needed at all (Avalonia draws colour emoji
    /// natively). A star-width GridLength has no such shortcut, so this one is a real port.</para>
    ///
    /// <para>0..1 fraction -> a star <see cref="GridLength"/>. Paired with an
    /// <c>Invert="True"</c> instance the two fill and pad a two-column gauge grid.</para>
    /// </summary>
    public sealed class FractionToStarConverter : IValueConverter
    {
        /// <summary>When true the converter returns the *remainder* (1 - fraction).</summary>
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double f = ToFraction(value);
            if (Invert) f = 1.0 - f;
            return new GridLength(f, GridUnitType.Star);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;

        /// <summary>Clamps anything sane-looking into 0..1. Never throws, never returns NaN.</summary>
        internal static double ToFraction(object? value)
        {
            double f;
            switch (value)
            {
                case double d: f = d; break;
                case float ff: f = ff; break;
                case int i: f = i; break;
                case long l: f = l; break;
                case decimal m: f = (double)m; break;
                default: return 0.0;
            }
            if (double.IsNaN(f) || double.IsInfinity(f)) return 0.0;
            if (f < 0.0) return 0.0;
            if (f > 1.0) return 1.0;
            return f;
        }
    }
}
