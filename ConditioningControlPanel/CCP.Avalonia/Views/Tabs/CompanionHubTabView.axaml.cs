using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class CompanionHubTabView : UserControl
{
    public CompanionHubTabView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void MuteToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle && DataContext is CompanionHubTabViewModel vm)
        {
            vm.MuteAvatarCommand.Execute(toggle.IsChecked == true);
        }
    }
}
