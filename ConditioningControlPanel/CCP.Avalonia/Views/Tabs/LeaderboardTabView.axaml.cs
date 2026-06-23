using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class LeaderboardTabView : UserControl
{
    public LeaderboardTabView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is LeaderboardTabViewModel vm)
        {
            vm.RequestSelectTab += OnRequestSelectTab;
        }
    }

    private void OnRequestSelectTab(string key)
    {
        if (TopLevel.GetTopLevel(this) is Window window
            && window.DataContext is MainWindowViewModel mainVm)
        {
            mainVm.SelectTabCommand.Execute(key);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is LeaderboardTabViewModel vm)
        {
            vm.RequestSelectTab -= OnRequestSelectTab;
        }
    }
}
