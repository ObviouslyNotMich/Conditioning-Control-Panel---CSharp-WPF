using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BugReport;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the bug-report dialog. Collects description + steps from the user,
/// shows the outgoing payload preview, and submits through the cross-platform bug-report service.
/// </summary>
public partial class BugReportWindow : Window
{
    private readonly ILogger<BugReportWindow> _logger;
    private readonly IBugReportService _service;
    private readonly DispatcherTimer _enableTimer;
    private readonly IDialogService? _dialogService;
    private bool _submitted;
    private bool _submitting;

    public BugReportWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<BugReportWindow>>();
        _service = App.Services.GetRequiredService<IBugReportService>();
        _dialogService = App.Services?.GetService<IDialogService>();

        _enableTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _enableTimer.Tick += (_, _) =>
        {
            _enableTimer.Stop();
            if (!_submitting && !_submitted)
            {
                BtnSend.IsEnabled = true;
            }
        };

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        RefreshPreview();
        _enableTimer.Start();
        TxtDescription.Focus();
    }

    private void OnFieldChanged(object? sender, RoutedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        try
        {
            var draft = _service.CreateDraft(
                TxtDescription.Text ?? "",
                TxtSteps.Text ?? "",
                ChkIncludeAppLog.IsChecked == true);

            var m = draft.Metadata;
            TxtMetadataSummary.Text =
                $"app_version : {m.AppVersion}\n" +
                $"os          : {m.Os}\n" +
                $".NET        : {m.Dotnet}\n" +
                $"language    : {m.Language}\n" +
                $"active_mod  : {m.ActiveModId}";

            TxtScrubberCounts.Text = Loc.GetF(
                "bug_report_scrubber_count",
                draft.Counts.Paths,
                draft.Counts.Emails,
                draft.Counts.Tokens,
                draft.Counts.AppData);

            TxtPreview.Text = _service.RenderPreview(draft);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[BugReport] preview render failed");
        }
    }

    private async void BtnSend_Click(object? sender, RoutedEventArgs e)
    {
        if (_submitting || _submitted) return;
        _submitting = true;
        BtnSend.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        TxtStatus.Text = "…";

        try
        {
            var draft = _service.CreateDraft(
                TxtDescription.Text ?? "",
                TxtSteps.Text ?? "",
                ChkIncludeAppLog.IsChecked == true);

            var result = await _service.SubmitAsync(draft).ConfigureAwait(true);
            _submitted = true;

            switch (result.Outcome)
            {
                case SubmitOutcome.Success:
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowMessageAsync(
                            Loc.Get("bug_report_title"),
                            Loc.GetF("bug_report_success_toast", result.Token ?? "(no token)"),
                            DialogSeverity.Info).ConfigureAwait(true);
                    }
                    Close();
                    break;

                case SubmitOutcome.SavedPending:
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowMessageAsync(
                            Loc.Get("bug_report_title"),
                            Loc.GetF("bug_report_saved_pending_toast", result.Token ?? ""),
                            DialogSeverity.Info).ConfigureAwait(true);
                    }
                    Close();
                    break;

                case SubmitOutcome.ValidationFailed:
                case SubmitOutcome.NetworkError:
                default:
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowMessageAsync(
                            Loc.Get("bug_report_title"),
                            Loc.Get("bug_report_error_toast") + "\n\n" + (result.ErrorMessage ?? ""),
                            DialogSeverity.Warning).ConfigureAwait(true);
                    }
                    _submitted = false;
                    _submitting = false;
                    BtnSend.IsEnabled = true;
                    BtnCancel.IsEnabled = true;
                    TxtStatus.Text = string.Empty;
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BugReport] submit failed");
            _ = _dialogService?.ShowMessageAsync(
                Loc.Get("bug_report_title"),
                Loc.Get("bug_report_error_toast") + "\n\n" + ex.Message,
                ConditioningControlPanel.Core.Platform.DialogSeverity.Error);
            _submitted = false;
            _submitting = false;
            BtnSend.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            TxtStatus.Text = string.Empty;
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_submitting) return;
        Close();
    }
}
