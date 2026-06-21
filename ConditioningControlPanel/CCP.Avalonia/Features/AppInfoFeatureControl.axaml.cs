using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Update;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class AppInfoFeatureControl : UserControl
{
    private readonly IUpdateService? _updateService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public AppInfoFeatureControl()
    {
        InitializeComponent();
        _updateService = App.Services.GetService<IUpdateService>();
        _dialogService = App.Services.GetService<IDialogService>();
        _logger = App.Services.GetService<IAppLogger>();
        Loaded += OnLoaded;
    }

    public StackPanel AccountSectionsHost => ExternalSectionsHost;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? LocalizationManager.Instance.Get("msg_version_unknown");
        TxtVersion.Text = $"v{version}";
        TxtProduct.Text = LocalizationManager.Instance.Get("app_title");
    }

    private async void BtnCheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        TxtProduct.Text = LocalizationManager.Instance.Get("status_checking_for_updates");

        try
        {
            var update = _updateService != null ? await _updateService.CheckForUpdatesAsync(forceCheck: true) : null;
            if (update == null)
            {
                await (_dialogService?.ShowMessageAsync(
                    LocalizationManager.Instance.Get("title_no_update"),
                    LocalizationManager.Instance.Get("msg_you_are_on_the_latest_version")) ?? Task.CompletedTask);
            }
            // If an update is available, the existing UpdateAvailable handler shows the notification dialog.
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "AppInfo: check updates failed");
            await (_dialogService?.ShowMessageAsync(
                LocalizationManager.Instance.Get("title_update_check"),
                LocalizationManager.Instance.GetF("msg_update_check_failed", ex.Message)) ?? Task.CompletedTask);
        }
    }

    private void BtnReportBug_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = new BugReportWindow();
            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "AppInfo: failed to open BugReportWindow");
            _dialogService?.ShowMessageAsync(
                LocalizationManager.Instance.Get("title_bug_report"),
                LocalizationManager.Instance.GetF("msg_bug_report_open_failed", ex.Message));
        }
    }
}
