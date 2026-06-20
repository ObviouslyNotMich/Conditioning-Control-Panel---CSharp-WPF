using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Features;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class LevelFeaturesTabView : UserControl
{
    public LevelFeaturesTabView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private LevelFeaturesTabViewModel? ViewModel => DataContext as LevelFeaturesTabViewModel;

    private void FeatureCard_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not FeatureCard card || ViewModel is null)
            return;

        switch (card.Tag?.ToString())
        {
            case "BubbleCount":
                ViewModel.OpenBubbleCountDetailsCommand.Execute(null);
                break;
            case "BouncingText":
                ViewModel.OpenBouncingTextDetailsCommand.Execute(null);
                break;
            case "BrainDrain":
                ViewModel.OpenBrainDrainDetailsCommand.Execute(null);
                break;
            case "MindWipe":
                ViewModel.OpenMindWipeDetailsCommand.Execute(null);
                break;
        }
    }

    private void FeatureCard_ToggleRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is not FeatureCard card || ViewModel is null)
            return;

        switch (card.Tag?.ToString())
        {
            case "BubbleCount":
                ViewModel.ToggleBubbleCountEnabledCommand.Execute(null);
                break;
            case "BouncingText":
                ViewModel.ToggleBouncingTextEnabledCommand.Execute(null);
                break;
            case "BrainDrain":
                ViewModel.ToggleBrainDrainEnabledCommand.Execute(null);
                break;
            case "MindWipe":
                ViewModel.ToggleMindWipeEnabledCommand.Execute(null);
                break;
        }
    }
}
