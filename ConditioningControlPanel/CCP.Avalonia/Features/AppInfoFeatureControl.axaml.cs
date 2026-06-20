using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class AppInfoFeatureControl : UserControl
{
    public AppInfoFeatureControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public StackPanel AccountSectionsHost => ExternalSectionsHost;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? LocalizationManager.Instance.Get("msg_version_unknown");
        TxtVersion.Text = $"v{version}";
        TxtProduct.Text = LocalizationManager.Instance.Get("app_title");
    }

    private void BtnCheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: wire to update installer once dialog service is ready.
        TxtProduct.Text = LocalizationManager.Instance.Get("status_checking_for_updates");
    }

    private void BtnReportBug_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Avalonia bug-report dialog once ported.
    }
}
