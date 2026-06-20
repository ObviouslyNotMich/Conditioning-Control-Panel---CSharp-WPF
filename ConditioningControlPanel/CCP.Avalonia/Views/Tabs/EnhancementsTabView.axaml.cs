using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class EnhancementsTabView : UserControl
{
    public EnhancementsTabView()
    {
        AvaloniaXamlLoader.Load(this);

        var scroller = this.FindControl<ScrollViewer>("SkillTreeScroller");
        if (scroller != null)
        {
            scroller.PointerWheelChanged += SkillTreeScroller_PointerWheelChanged;
        }
    }

    /// <summary>
    /// Redirects vertical mouse wheel scrolling to horizontal scrolling for the skill tree.
    /// </summary>
    private void SkillTreeScroller_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        var delta = e.Delta.Y;
        if (delta == 0)
            return;

        scrollViewer.Offset = scrollViewer.Offset.WithX(scrollViewer.Offset.X - delta * 40);
        e.Handled = true;
    }
}
