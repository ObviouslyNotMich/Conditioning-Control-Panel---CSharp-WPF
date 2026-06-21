using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Converters;

/// <summary>
/// Converts a <see cref="Preset"/> into a compact list of feature-stat glyphs
/// suitable for display inside a <see cref="Features.PresetCard"/>.
/// </summary>
public sealed class PresetToFeatureStatsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Preset preset)
            return Array.Empty<string>();

        var stats = new List<string>();

        if (preset.FlashEnabled) stats.Add("\u26a1");          // Flash
        if (preset.MandatoryVideosEnabled) stats.Add("\ud83c\udfac"); // Video
        if (preset.SubliminalEnabled) stats.Add("\ud83d\udcad");    // Subliminal
        if (preset.SubAudioEnabled || preset.AudioDuckingEnabled) stats.Add("\ud83d\udd0a"); // Audio
        if (preset.SpiralEnabled || preset.PinkFilterEnabled) stats.Add("\ud83c\udf00"); // Overlays
        if (preset.BubblesEnabled) stats.Add("\ud83e\udee7");       // Bubbles
        if (preset.LockCardEnabled) stats.Add("\ud83d\udd12");      // Lock Card
        if (preset.BubbleCountEnabled) stats.Add("\ud83d\udd22");   // Bubble Count
        if (preset.BouncingTextEnabled) stats.Add("\ud83d\udcfa");  // Bouncing Text
        if (preset.MindWipeEnabled) stats.Add("\ud83e\udde0");      // Mind Wipe
        if (preset.BrainDrainEnabled) stats.Add("\ud83d\udca7");    // Brain Drain

        return stats;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
