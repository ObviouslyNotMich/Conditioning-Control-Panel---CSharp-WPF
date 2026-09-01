using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Modal bug report dialog. Collects description + steps from the user, shows the exact
    /// outgoing payload in a read-only preview, and submits when the user clicks Send.
    /// Send is disabled for 2 seconds after the window opens to force the user to look at the
    /// preview before submitting.
    ///
    /// PORTED from ConditioningControlPanel/Windows/BugReportWindow.xaml.cs. Deviations:
    ///  - BugReportService still lives in the WPF head, so the draft/preview/submit paths are
    ///    stubs with placeholder data; the success panel is shown with a placeholder token.
    ///  - Clipboard goes through TopLevel.Clipboard (async).
    ///  - The error MessageBox path is unreachable until the service is wired.
    /// </summary>
    public partial class BugReportWindow : Window
    {
        /// <summary>Mirrors BugReportService.ReportKind, which is still in the WPF head.</summary>
        public enum ReportKind { Bug, Suggestion }

        private readonly DispatcherTimer _enableTimer;
        private readonly ReportKind _kind;
        private bool _submitted;
        private bool _submitting;

        private readonly TextBox _txtDescription, _txtSteps, _txtPreview, _txtSuccessToken;
        private readonly TextBlock _txtMetadataSummary, _txtScrubberCounts, _txtStatus, _txtSuccessHeadline,
            _lblSuccessTokenLabel, _txtSuccessHint, _txtCopyToken;
        private readonly CheckBox _chkIncludeAppLog;
        private readonly Button _btnSend, _btnCancel, _btnSuccessDone;
        private readonly Border _successPanel;
        private readonly Grid _successTokenRow;

        public BugReportWindow() : this(ReportKind.Bug) { }

        public BugReportWindow(ReportKind kind)
        {
            AvaloniaXamlLoader.Load(this);
            _kind = kind;

            _txtDescription = this.FindControl<TextBox>("TxtDescription")!;
            _txtSteps = this.FindControl<TextBox>("TxtSteps")!;
            _txtPreview = this.FindControl<TextBox>("TxtPreview")!;
            _txtSuccessToken = this.FindControl<TextBox>("TxtSuccessToken")!;
            _txtMetadataSummary = this.FindControl<TextBlock>("TxtMetadataSummary")!;
            _txtScrubberCounts = this.FindControl<TextBlock>("TxtScrubberCounts")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _txtSuccessHeadline = this.FindControl<TextBlock>("TxtSuccessHeadline")!;
            _lblSuccessTokenLabel = this.FindControl<TextBlock>("LblSuccessTokenLabel")!;
            _txtSuccessHint = this.FindControl<TextBlock>("TxtSuccessHint")!;
            _txtCopyToken = this.FindControl<TextBlock>("TxtCopyToken")!;
            _chkIncludeAppLog = this.FindControl<CheckBox>("ChkIncludeAppLog")!;
            _btnSend = this.FindControl<Button>("BtnSend")!;
            _btnCancel = this.FindControl<Button>("BtnCancel")!;
            _btnSuccessDone = this.FindControl<Button>("BtnSuccessDone")!;
            _successPanel = this.FindControl<Border>("SuccessPanel")!;
            _successTokenRow = this.FindControl<Grid>("SuccessTokenRow")!;

            ApplyKind();

            _enableTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _enableTimer.Tick += (_, _) =>
            {
                _enableTimer.Stop();
                if (!_submitting && !_submitted)
                    _btnSend.IsEnabled = true;
            };

            _txtDescription.TextChanged += (_, _) => RefreshPreview();
            _txtSteps.TextChanged += (_, _) => RefreshPreview();
            _chkIncludeAppLog.IsCheckedChanged += (_, _) => RefreshPreview();
            _btnSend.Click += (_, _) => BtnSend_Click();
            _btnCancel.Click += (_, _) => { if (!_submitting) Close(); };
            this.FindControl<Button>("BtnCopyToken")!.Click += async (_, _) => await CopyTokenAsync();
            _btnSuccessDone.Click += (_, _) => Close();

            // WPF ran this from Loaded. The preview is filled here so the headless render shows
            // it; the timer and focus wait for the window to actually open.
            RefreshPreview();
            Opened += (_, _) => { _enableTimer.Start(); _txtDescription.Focus(); };
        }

        /// <summary>
        /// Word the dialog for its kind. Both kinds are set from code: these four controls carry
        /// no {loc:Str} binding, because a local Text set does not clear an Avalonia binding and
        /// the next language change would have reverted a suggestion dialog to bug wording.
        /// Suggestion mode also hides the defect-only fields (repro steps, log opt-in, counts).
        /// </summary>
        private void ApplyKind()
        {
            var suggestion = _kind == ReportKind.Suggestion;
            Title = Loc.Get(suggestion ? "suggestion_title" : "bug_report_title");
            this.FindControl<TextBlock>("TxtHeaderTitle")!.Text = Title;
            this.FindControl<TextBlock>("TxtPrivacyNotice")!.Text = Loc.Get(suggestion ? "suggestion_privacy_notice" : "bug_report_privacy_notice");
            this.FindControl<TextBlock>("LblDescription")!.Text = Loc.Get(suggestion ? "suggestion_description_label" : "bug_report_description_label");

            this.FindControl<TextBlock>("LblSteps")!.IsVisible = !suggestion;
            _txtSteps.IsVisible = !suggestion;
            _chkIncludeAppLog.IsVisible = !suggestion;
            _txtScrubberCounts.IsVisible = !suggestion;
        }

        private void RefreshPreview()
        {
            // ponytail: needs BugReportService.CreateDraft/RenderPreview, wired when it moves to Core.
            // Until then the metadata is what this process can see and the counts are zero.
            var appVersion = typeof(BugReportWindow).Assembly.GetName().Version?.ToString(3) ?? "?";
            _txtMetadataSummary.Text =
                $"app_version : {appVersion}\n" +
                $"os          : {RuntimeInformation.OSDescription}\n" +
                $".NET        : {Environment.Version}\n" +
                $"language    : {LocalizationManager.Instance.CurrentLanguage}\n" +
                $"active_mod  : (none)";

            _txtScrubberCounts.Text = Loc.GetF("bug_report_scrubber_count", 0, 0, 0, 0);

            _txtPreview.Text =
                $"kind        : {(_kind == ReportKind.Suggestion ? "suggestion" : "bug")}\n" +
                $"description : {_txtDescription.Text}\n" +
                $"steps       : {_txtSteps.Text}\n" +
                $"include_log : {_chkIncludeAppLog.IsChecked == true}\n" +
                _txtMetadataSummary.Text;
        }

        private void BtnSend_Click()
        {
            if (_submitting || _submitted) return;
            _submitting = true;
            _btnSend.IsEnabled = false;
            _btnCancel.IsEnabled = false;
            _txtStatus.Text = "…";

            // ponytail: needs BugReportService.SubmitAsync, wired when it moves to Core. The
            // placeholder token below exists so the success panel can be reached and rendered.
            _submitted = true;
            var token = "BUG-PLACEHOLDER";
            var isSuggestion = _kind == ReportKind.Suggestion;
            ShowSuccessPanel(
                Loc.GetF(isSuggestion ? "suggestion_success_toast" : "bug_report_success_toast", token),
                token);
        }

        /// <summary>
        /// Swap the form for the success panel (#769). The report number stays on screen in a
        /// read-only, selectable box with a Copy button until the user clicks Done. A 202 without
        /// a token still shows the headline, minus the box.
        /// </summary>
        private void ShowSuccessPanel(string headline, string? token)
        {
            _txtSuccessHeadline.Text = headline;

            bool hasToken = !string.IsNullOrWhiteSpace(token);
            _txtSuccessToken.Text = token ?? string.Empty;

            _lblSuccessTokenLabel.IsVisible = hasToken;
            _successTokenRow.IsVisible = hasToken;
            _txtSuccessHint.IsVisible = hasToken;

            _successPanel.IsVisible = true;
            _btnSuccessDone.Focus();
        }

        private async System.Threading.Tasks.Task CopyTokenAsync()
        {
            try
            {
                var token = _txtSuccessToken.Text;
                if (string.IsNullOrWhiteSpace(token) || Clipboard is null) return;
                await Clipboard.SetTextAsync(token);
                _txtCopyToken.Text = Loc.Get("btn_copied");
            }
            catch
            {
                // Clipboard can be locked by another process — never crash the dialog over it.
            }
        }
    }
}
