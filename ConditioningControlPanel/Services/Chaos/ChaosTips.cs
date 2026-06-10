using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Themed hover card for boon art — one look everywhere a boon image appears
/// (draft offers, the Warren shelves, the Diary, pocket chips). Tooltips stay
/// non-layered app-wide (no drop shadow, see App.xaml) so showing one can never
/// collide with a layered window on the render thread.
/// </summary>
public static class ChaosTips
{
    /// <summary>Attach a title/desc hover card to <paramref name="target"/>. <paramref name="extra"/>
    /// renders as a gold capstone line; <paramref name="accent"/> tints the title (default pink).</summary>
    public static void Attach(FrameworkElement target, string title, string? desc, string? extra = null, Color? accent = null)
    {
        var a = accent ?? Color.FromRgb(0xFF, 0x69, 0xB4);
        var sp = new StackPanel { MaxWidth = 260 };
        sp.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeights.Bold, FontSize = 13,
            Foreground = new SolidColorBrush(a)
        });
        if (!string.IsNullOrWhiteSpace(desc))
            sp.Children.Add(new TextBlock
            {
                Text = desc, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xE0, 0xE0, 0xF0))
            });
        if (!string.IsNullOrWhiteSpace(extra))
            sp.Children.Add(new TextBlock
            {
                Text = extra, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00))
            });

        target.ToolTip = new ToolTip
        {
            Content = sp,
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x15, 0x12, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x69, 0xB4)),
            Foreground = Brushes.White,
            Padding = new Thickness(10, 8, 10, 8),
        };
        ToolTipService.SetInitialShowDelay(target, 250);
        ToolTipService.SetShowDuration(target, 30000);
    }
}
