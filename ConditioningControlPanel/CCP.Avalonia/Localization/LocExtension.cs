using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Localization;

/// <summary>
/// Avalonia XAML markup extension for localized strings.
/// Usage: Content="{loc:Str btn_cancel}"
///
/// Register namespace in XAML:
///   xmlns:loc="clr-namespace:ConditioningControlPanel.Avalonia.Localization;assembly=CCP.Avalonia"
/// </summary>
public class StrExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public StrExtension() { }

    public StrExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return string.Empty;

        // Create a binding to LocalizationManager's indexer so it updates on language change.
        var binding = new Binding($"[{Key}]")
        {
            Source = Core.Localization.LocalizationManager.Instance,
            Mode = BindingMode.OneWay
        };

        return binding;
    }
}
