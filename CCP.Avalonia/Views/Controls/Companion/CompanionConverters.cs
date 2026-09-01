using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/CompanionConverters.cs — the
    /// one converter of that file the Engine Room needs. SHARED: any other companion view with a
    /// segmented dial (Z5's intensity dial) binds through this same converter, so a sibling port
    /// may add its neighbours to this file rather than starting a second one.
    ///
    /// <para>Two-way by design: the segment RadioButtons bind IsChecked to an enum property with a
    /// per-button ConverterParameter, so ConvertBack is what actually writes the pick back.</para>
    ///
    /// <para>The WPF file's visibility converters do NOT cross: Avalonia binds IsVisible to a bool,
    /// so <c>CmpBoolToVis</c> is a plain binding, <c>CmpBoolToVisInverse</c> is <c>{Binding !X}</c>,
    /// <c>CmpHasContentToVis</c> is <c>ObjectConverters.IsNotNull</c>, and <c>CmpEnumToVis</c> is
    /// this class again.</para>
    /// </summary>
    public sealed class CompanionEnumEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null && parameter != null &&
               string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
            {
                try
                {
                    var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
                    if (t.IsEnum) return Enum.Parse(t, parameter.ToString()!, ignoreCase: true);
                }
                catch (ArgumentException) { /* unparseable parameter — leave the source alone */ }
                return parameter;
            }
            return BindingOperations.DoNothing;
        }
    }
}
