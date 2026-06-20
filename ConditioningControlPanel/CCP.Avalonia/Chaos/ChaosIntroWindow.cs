using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Styling;
using global::Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosIntroWindow: the one-time, spoiler-free intro card shown the first
/// time the Dollhouse opens. Built in code so it needs no XAML.
/// </summary>
public sealed class ChaosIntroWindow : Window
{
    public bool? DialogResult { get; set; }

    public ChaosIntroWindow()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        Width = 560;
        ShowInTaskbar = false;
        ShowActivated = true;

        var pink = Color.FromRgb(0xE8, 0x43, 0x93);
        var stack = new StackPanel();

        // hero art
        var hero = ChaosArt.Resolve("guide", "intro");
        if (hero != null)
        {
            var img = new Image { Source = hero, Stretch = Stretch.UniformToFill, Height = 220 };
            var heroHost = new Border
            {
                Height = 220,
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Child = img,
                ClipToBounds = true,
            };
            heroHost.Loaded += (_, _) =>
            {
                try
                {
                    heroHost.Clip = new RectangleGeometry(
                        new Rect(0, 0, heroHost.Bounds.Width, heroHost.Bounds.Height), 14, 14);
                }
                catch { }
            };
            stack.Children.Add(heroHost);
        }

        var title = new TextBlock
        {
            Text = "🐇 DOWN THE RABBIT HOLE",
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, hero != null ? 18 : 30, 0, 2),
        };
        stack.Children.Add(title);
        stack.Children.Add(new TextBlock
        {
            Text = "you don't have to understand it. you just have to fall.",
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
            FontStyle = FontStyle.Italic,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var rules = new StackPanel { Margin = new Thickness(46, 0, 46, 4) };
        void Rule(string glyph, Color color, string? verb, string head, string rest)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 7) };
            row.Children.Add(new TextBlock
            {
                Text = glyph,
                FontSize = 22,
                Width = 38,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(color),
            });
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(verb))
            {
                col.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, color.R, color.G, color.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 1, 7, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 3),
                    Child = new TextBlock
                    {
                        Text = verb,
                        Foreground = new SolidColorBrush(color),
                        FontWeight = FontWeight.Bold,
                        FontSize = 11,
                        FontFamily = new FontFamily("Consolas"),
                    },
                });
            }
            var text = new TextBlock
            {
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 380,
            };
            text.Inlines?.Add(new Run(head) { Foreground = Brushes.White, FontWeight = FontWeight.Bold });
            text.Inlines?.Add(new Run(rest) { Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xC8, 0xC8, 0xDE)) });
            col.Children.Add(text);
            row.Children.Add(col);
            rules.Children.Add(row);
        }
        Rule("🫧", Color.FromRgb(0xFF, 0xD0, 0xE8), "LEFT-CLICK", "pop the treats. ", "a click is enough. they feed your streak.");
        Rule("◉", Color.FromRgb(0xFF, 0xD2, 0x28), "PRESS & HOLD", "hold the burning ones. ", "press and keep pressing until they snap. let one finish its trance and it goes off.");
        Rule("🌊", Color.FromRgb(0x7A, 0xE0, 0xFF), "RIGHT-CLICK", "ripple the water. ", "a right-click near the bubbles sends out a wave — treats pop, trances snap, rabbits go flying. it takes a while to gather another.");
        Rule("🐇", Color.FromRgb(0xFF, 0x69, 0xB4), null, "follow the white rabbit. ", "everything else down there is yours to find out.");
        stack.Children.Add(rules);

        var btn = new Button
        {
            Content = "i understand. take me down",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 13, 0, 13),
            Margin = new Thickness(46, 16, 46, 26),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xE8, 0x43, 0x93), 0),
                    new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 1),
                }
            },
        };
        btn.Resources.Add(typeof(Border), new Style
        {
            Selector = Selectors.OfType<Border>(null),
            Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(12)) }
        });
        btn.Click += (_, _) => { DialogResult = true; Close(); };
        stack.Children.Add(btn);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x2A)),
            BorderBrush = new SolidColorBrush(pink),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Child = stack,
        };

        Opacity = 0;
        Opened += (_, _) =>
        {
            try
            {
                _ = new OpacityFade(this, 0, 1, 420);
            }
            catch { Opacity = 1; }
        };
    }
}
