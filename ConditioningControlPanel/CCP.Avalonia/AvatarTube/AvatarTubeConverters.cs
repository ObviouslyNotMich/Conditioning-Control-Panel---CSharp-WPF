using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public class BoolToBrushConverter : IValueConverter
    {
        public IBrush TrueBrush { get; set; } = Brushes.Pink;
        public IBrush FalseBrush { get; set; } = Brushes.Gray;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? TrueBrush : FalseBrush;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class BoolToHorizontalAlignmentConverter : IValueConverter
    {
        public HorizontalAlignment TrueValue { get; set; } = HorizontalAlignment.Right;
        public HorizontalAlignment FalseValue { get; set; } = HorizontalAlignment.Left;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? TrueValue : FalseValue;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
