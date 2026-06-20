using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class RemoteControlTabView : UserControl
{
    public RemoteControlTabView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnTierCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string tier) return;
        if (DataContext is not RemoteControlTabViewModel vm) return;

        vm.SelectedTier = tier;
    }
}
