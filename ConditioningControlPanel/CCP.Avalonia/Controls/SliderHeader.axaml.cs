using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Controls;

public partial class SliderHeader : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SliderHeader, string>(nameof(Label), defaultValue: "");

    public static readonly StyledProperty<string> ValueTextProperty =
        AvaloniaProperty.Register<SliderHeader, string>(nameof(ValueText), defaultValue: "");

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string ValueText
    {
        get => GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public SliderHeader()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
