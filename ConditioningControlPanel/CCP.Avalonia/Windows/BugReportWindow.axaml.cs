using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the bug-report dialog. Collects description + steps from the user,
/// shows the outgoing payload preview, and submits through a stubbed service.
/// </summary>
public partial class BugReportWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private readonly BugReportService _service;
    private readonly DispatcherTimer _enableTimer;
    private readonly IDialogService? _dialogService;
    private bool _submitted;
    private bool _submitting;

    public BugReportWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_service = new BugReportService();
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
                TxtDescription.Text,
                TxtSteps.Text,
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
            _logger?.Warning(ex, "[BugReport] preview render failed");
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
                TxtDescription.Text,
                TxtSteps.Text,
                ChkIncludeAppLog.IsChecked == true);

            var result = await _service.SubmitAsync(draft).ConfigureAwait(true);
            _submitted = true;

            switch (result.Outcome)
            {
                case BugReportService.SubmitOutcome.Success:
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowMessageAsync(
                            Loc.Get("bug_report_title"),
                            Loc.GetF("bug_report_success_toast", result.Token ?? "(no token)"),
                            DialogSeverity.Info).ConfigureAwait(true);
                    }
                    Close();
                    break;

                case BugReportService.SubmitOutcome.SavedPending:
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowMessageAsync(
                            Loc.Get("bug_report_title"),
                            Loc.GetF("bug_report_saved_pending_toast", result.Token ?? ""),
                            DialogSeverity.Info).ConfigureAwait(true);
                    }
                    Close();
                    break;

                case BugReportService.SubmitOutcome.ValidationFailed:
                case BugReportService.SubmitOutcome.NetworkError:
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
            _logger?.Error(ex, "[BugReport] submit failed");
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

    /// <summary>
    /// Minimal in-process stub for the legacy WPF BugReportService.
    /// TODO: replace with the real cross-platform service once it is extracted to CCP.Core.
    /// </summary>
    private sealed class BugReportService
    {
        public enum SubmitOutcome
        {
            Success,
            SavedPending,
            ValidationFailed,
            NetworkError
        }

        public sealed record SubmitResult(SubmitOutcome Outcome, string? Token = null, string? ErrorMessage = null);

        public sealed class DraftMetadata
        {
            public string AppVersion { get; set; } = CCPCore.Version;
            public string Os { get; set; } = Environment.OSVersion.ToString();
            public string Dotnet { get; set; } = Environment.Version.ToString();
            public string Language { get; set; } = "en";
            public string ActiveModId { get; set; } = "";
        }

        public sealed class DraftCounts
        {
            public int Paths { get; set; }
            public int Emails { get; set; }
            public int Tokens { get; set; }
            public int AppData { get; set; }
        }

        public sealed class Draft
        {
            public DraftMetadata Metadata { get; } = new();
            public DraftCounts Counts { get; } = new();
            public string Description { get; set; } = "";
            public string Steps { get; set; } = "";
            public bool IncludeLog { get; set; }
        }

        public Draft CreateDraft(string? description, string? steps, bool includeLog)
        {
            var draft = new Draft
            {
                Description = description ?? "",
                Steps = steps ?? "",
                IncludeLog = includeLog
            };

            try
            {
                var settings = App.Services?.GetService<ISettingsService>()?.Current;
                draft.Metadata.Language = settings?.Language ?? "en";
            }
            catch { /* designer/no-services fallback */ }

            draft.Counts.Paths = CountMatches(draft.Description + draft.Steps, @"[A-Za-z]:\\");
            draft.Counts.Emails = CountMatches(draft.Description + draft.Steps, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b");
            draft.Counts.Tokens = CountMatches(draft.Description + draft.Steps, @"\b[a-f0-9]{32,}\b");
            draft.Counts.AppData = includeLog ? 1 : 0;

            return draft;
        }

        public string RenderPreview(Draft draft)
        {
            return $"version: {draft.Metadata.AppVersion}\n" +
                   $"os: {draft.Metadata.Os}\n" +
                   $"language: {draft.Metadata.Language}\n" +
                   $"include_log: {draft.IncludeLog}\n" +
                   $"description:\n{draft.Description}\n\n" +
                   $"steps:\n{draft.Steps}";
        }

        public async Task<SubmitResult> SubmitAsync(Draft draft)
        {
            await Task.Delay(500).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(draft.Description))
            {
                return new SubmitResult(SubmitOutcome.ValidationFailed, null, "Description is required.");
            }

            // TODO: perform real network submit / save-pending once the backend service is available.
            return new SubmitResult(SubmitOutcome.Success, Guid.NewGuid().ToString("N")[..8].ToUpperInvariant());
        }

        private static int CountMatches(string text, string pattern)
        {
            try
            {
                return System.Text.RegularExpressions.Regex.Matches(text, pattern).Count;
            }
            catch
            {
                return 0;
}
        }
    }
}
