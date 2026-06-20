using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class DeeperTabView : UserControl
{
    public DeeperTabView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void DeeperRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;

        if (sender is Border border && border.DataContext is DeeperLibraryRowViewModel row)
        {
            row.OpenCommand.Execute(null);
        }
    }
}
