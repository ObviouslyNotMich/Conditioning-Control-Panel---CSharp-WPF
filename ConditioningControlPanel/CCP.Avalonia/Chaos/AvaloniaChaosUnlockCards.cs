using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>The three lifetime-boon shelves. Mirrors WPF ChaosBoonCategory.</summary>
public enum ChaosBoonCategory { Skill, Accessory, Utility }

/// <summary>Everything one unlock card renders: ribbon + accent, the item, and a context line.</summary>
public sealed class ChaosUnlockCardData
{
    public string Ribbon = "";
    public Color Accent = Color.FromRgb(0xE8, 0x43, 0x93);
    public string Title = "";
    public string Desc = "";
    public string? Flavor;
    public string? Context;
    public string Glyph = "◈";
    public IImage? Icon;
}

/// <summary>
/// Avalonia port of ChaosUnlockCards: data builders + shared card visual.
/// The data builders are stubbed because the WPF catalogue services
/// (ChaosLifetimeBoons, ChaosUpgrades, ChaosMeta) live outside CCP.Core
/// and are not reachable from CCP.Avalonia. The shared BuildCardVisual
/// is fully functional and used by the Avalonia unlock overlay.
/// </summary>
public static class ChaosUnlockCards
{
    private static readonly Color ToyAccent = Color.FromRgb(0x7A, 0xFF, 0xD2);
    private static readonly Color AccessoryAccent = Color.FromRgb(0xFF, 0xD2, 0x7A);
    private static readonly Color CharmAccent = Color.FromRgb(0x7A, 0xE0, 0xFF);
    private static readonly Color HabitAccent = Color.FromRgb(0x9C, 0xE8, 0xA0);
    private static readonly Color PocketAccent = Color.FromRgb(0xE8, 0x43, 0x93);
    private static readonly Color CapstoneAccent = Color.FromRgb(0xFF, 0xC8, 0x3C);

    private static (string ribbon, Color accent) ByCategory(ChaosBoonCategory cat) => cat switch
    {
        ChaosBoonCategory.Skill => ("NEW TOY UNLOCKED", ToyAccent),
        ChaosBoonCategory.Accessory => ("NEW ACCESSORY UNLOCKED", AccessoryAccent),
        _ => ("NEW CHARM UNLOCKED", CharmAccent),
    };

    /// <summary>Stub: returns null because the WPF lifetime-boon catalogue is unavailable here.</summary>
    public static ChaosUnlockCardData? ForBoonUnlock(string id)
    {
        // TODO: wire Avalonia lifetime-boon catalogue once it moves to CCP.Core.
        return null;
    }

    /// <summary>Stub: returns null because the WPF lifetime-boon catalogue is unavailable here.</summary>
    public static ChaosUnlockCardData? ForCapstone(string id)
    {
        // TODO: wire Avalonia lifetime-boon catalogue once it moves to CCP.Core.
        return null;
    }

    /// <summary>Stub: returns null because the WPF habit catalogue is unavailable here.</summary>
    public static ChaosUnlockCardData? ForHabit(string id)
    {
        // TODO: wire Avalonia habit catalogue once it moves to CCP.Core.
        return null;
    }

    /// <summary>Stub: returns a minimal pocket card.</summary>
    public static ChaosUnlockCardData ForPocket(bool isToy, string label, string line)
    {
        string kind = isToy ? "toy" : "accessory";
        return new ChaosUnlockCardData
        {
            Ribbon = "POCKET SEWN",
            Accent = PocketAccent,
            Title = label,
            Desc = $"you can now carry one {kind} into the descent. pick yours from the BAG.",
            Flavor = line,
            Glyph = "👝",
        };
    }

    /// <summary>Stub: returns null because the WPF catalogues are unavailable here.</summary>
    public static ChaosUnlockCardData? ForLesson(string id)
    {
        // TODO: wire Avalonia catalogues once they move to CCP.Core.
        return null;
    }

    private static (string cue, float vol) CueFor(ChaosUnlockCardData d) => d.Ribbon switch
    {
        "CAPSTONE REACHED" => ("capstone_reached", 0.7f),
        "POCKET SEWN" => ("pocket_sewn", 0.7f),
        _ => ("unlock_card", 0.65f),
    };

    /// <summary>The card itself — shared by the Hub layer and the mid-run overlay.</summary>
    public static Border BuildCardVisual(ChaosUnlockCardData d, double width = 400)
    {
        var accent = new SolidColorBrush(d.Accent);
        var accentDim = new SolidColorBrush(Color.FromArgb(0xCC, d.Accent.R, d.Accent.G, d.Accent.B));

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = d.Ribbon,
            Foreground = accent,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeight.Bold,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        Control icon;
        if (d.Icon != null)
        {
            icon = new Image { Source = d.Icon, Width = 64, Height = 64, Stretch = Stretch.Uniform };
        }
        else
        {
            icon = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x28, d.Accent.R, d.Accent.G, d.Accent.B)),
                Child = new TextBlock
                {
                    Text = d.Glyph,
                    FontSize = 30,
                    Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }
        icon.VerticalAlignment = VerticalAlignment.Top;
        var iconScale = new ScaleTransform(0.4, 0.4);
        icon.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        icon.RenderTransform = iconScale;
        row.Children.Add(icon);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = d.Title,
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = d.Desc,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xEE)),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        if (!string.IsNullOrWhiteSpace(d.Flavor))
        {
            text.Children.Add(new TextBlock
            {
                Text = d.Flavor,
                Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xA0, 0xA0, 0xC0)),
                FontStyle = FontStyle.Italic,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 0),
            });
        }
        if (!string.IsNullOrWhiteSpace(d.Context))
        {
            text.Children.Add(new TextBlock
            {
                Text = "→ " + d.Context,
                Foreground = accentDim,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 9, 0, 0),
            });
        }
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        stack.Children.Add(row);

        var border = new Border
        {
            Width = width,
            Child = stack,
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1C, 0x1A, 0x36)),
            BorderBrush = accentDim,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 14, 18, 16),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = Color.FromArgb(0x80, d.Accent.R, d.Accent.G, d.Accent.B),
                Blur = 24,
                Spread = 0,
            }),
        };

        bool played = false;
        border.Loaded += (_, _) =>
        {
            if (played) return;
            played = true;
            try
            {
                var (cue, vol) = CueFor(d);
                AvaloniaChaosSfx.Play(cue, vol);

                // TODO: Avalonia replacements for the accent-glow flare and icon pop animations.
                // The card already has a static BoxShadow; animated scaling/pulsing is deferred.
            }
            catch (Exception ex) { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("Unlock card flair failed: {E}", ex.Message); }
        };
        return border;
}

}
