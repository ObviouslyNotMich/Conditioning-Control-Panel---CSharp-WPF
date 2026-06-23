using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs
{
    /// <summary>
    /// Onboarding wizard for the local-AI (Ollama) provider. Drives users through
    /// detect → consent → install Ollama → pull model → smoke test → done.
    /// On success, flips <c>AiProvider</c>/<c>AiModel</c> in settings and saves.
    /// </summary>
    public partial class LocalAiSetupWizard : Window
    {
        private const string DefaultModel = "qwen3.5:latest";
        private const string ManualInstallUrl = "https://ollama.com/download";

        private enum Step
        {
            Detecting,
            Consent,
            DownloadInstaller,
            Installing,
            PullModel,
            SmokeTest,
            Done,
            Error
        }

        private readonly IOllamaSetupService _ollamaSetupService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<LocalAiSetupWizard> _logger;

        private Step _step = Step.Detecting;
        private CancellationTokenSource? _cts;
        private string _targetModel = DefaultModel;
        private bool _wizardComplete;

        public bool LocalAiReady { get; private set; }
        public string SelectedModel => _targetModel;
        public bool? Result { get; private set; }

        public LocalAiSetupWizard()
        {
            InitializeComponent();

            _ollamaSetupService = App.Services.GetRequiredService<IOllamaSetupService>();
            _settingsService = App.Services.GetRequiredService<ISettingsService>();
            _logger = App.Services.GetRequiredService<ILogger<LocalAiSetupWizard>>();

            _targetModel = ResolveStartingModel();
            TxtAdvancedModel.Text = _targetModel;
            UpdateConsentDiskNote();

            Opened += async (_, __) => await StartDetectAsync();
        }

        private string ResolveStartingModel()
        {
            var saved = _settingsService.Current?.CompanionPrompt?.AiModel;
            return string.IsNullOrWhiteSpace(saved) ? DefaultModel : saved!.Trim();
        }

        private void UpdateConsentDiskNote()
        {
            // The default model is ~6.6 GB; for unknown custom models we just say "varies."
            TxtConsentLine2.Text = string.Equals(_targetModel, DefaultModel, StringComparison.OrdinalIgnoreCase)
                ? Loc.GetF("label_local_ai_consent_pull_model_known", _targetModel, "~6.6 GB")
                : Loc.GetF("label_local_ai_consent_pull_model_custom", _targetModel);
        }

        /// <summary>
        /// Shows the wizard as a modal dialog and returns true when local AI is ready.
        /// </summary>
        public static async Task<bool?> ShowAsync(Window owner)
        {
            var wizard = new LocalAiSetupWizard();
            return await wizard.ShowDialog<bool?>(owner);
        }

        // -------- Step transitions --------

        private void Show(Step s)
        {
            _step = s;
            PanelDetecting.IsVisible = s == Step.Detecting;
            PanelConsent.IsVisible = s == Step.Consent;
            PanelDownloadInstaller.IsVisible = s == Step.DownloadInstaller;
            PanelInstalling.IsVisible = s == Step.Installing;
            PanelPullModel.IsVisible = s == Step.PullModel;
            PanelSmokeTest.IsVisible = s == Step.SmokeTest;
            PanelDone.IsVisible = s == Step.Done;
            PanelError.IsVisible = s == Step.Error;

            switch (s)
            {
                case Step.Detecting:
                    TxtStepTitle.Text = Loc.Get("label_local_ai_step_detecting");
                    TxtStepSubtitle.Text = Loc.Get("label_local_ai_step_detecting_sub");
                    BtnPrimary.IsVisible = false;
                    BtnSecondary.Content = Loc.Get("btn_cancel");
                    BtnSecondary.IsEnabled = true;
                    BtnSecondary.IsVisible = true;
                    break;
                case Step.Consent:
                    TxtStepTitle.Text = Loc.Get("label_local_ai_step_consent");
                    TxtStepSubtitle.Text = Loc.Get("label_local_ai_step_consent_sub");
                    BtnPrimary.IsVisible = true;
                    BtnPrimary.Content = Loc.Get("btn_continue");
                    BtnSecondary.Content = Loc.Get("btn_cancel");
                    BtnSecondary.IsVisible = true;
                    BtnPrimary.IsEnabled = true;
                    BtnSecondary.IsEnabled = true;
                    break;
                case Step.DownloadInstaller:
                    TxtStepTitle.Text = Loc.Get("label_local_ai_step_download");
                    TxtStepSubtitle.Text = Loc.Get("label_local_ai_step_download_sub");
                    BtnPrimary.IsVisible = false;
                    BtnSecondary.Content = Loc.Get("btn_cancel");
                    BtnSecondary.IsEnabled = true;
                    BtnSecondary.IsVisible = true;
                    break;
                case Step.Installing:
                    TxtStepTitle.Text = Loc.Get("label_local_ai_step_install");
                    TxtStepSubtitle.Text = Loc.Get("label_local_ai_step_install_sub");
                    BtnPrimary.IsVisible = false;
                    // Don't allow cancel during silent install — Ollama's NSIS installer
                    // doesn't roll back gracefully and a half-finished install is worse
                    // than a finished one the user can uninstall.
                    BtnSecondary.IsEnabled = false;
                    break;
                case Step.PullModel:
                    TxtStepTitle.Text = Loc.Get("label_local_ai_step_pull");
                    TxtStepSubtitle.Text = Loc.Get("label_local_ai_step_pull_sub");
                    BtnPrimary.IsVisible = false;
                    BtnSecondary.Content = Loc.Get("btn_cancel");
                    BtnSecondary.IsEnabled = true;
                    BtnSecondary.IsVisible = true;
                    break;
                case Step.SmokeTest:
                    TxtStepTitle.Text = Loc.Get("label_local_ai_step_smoke");
                    TxtStepSubtitle.Text = Loc.Get("label_local_ai_step_smoke_sub");
                    BtnPrimary.IsVisible = false;
                    BtnSecondary.IsEnabled = false;
                    break;
                case Step.Done:
                    TxtStepTitle.Text = Loc.Get("label_local_ai_step_done");
                    TxtStepSubtitle.Text = Loc.Get("label_local_ai_step_done_sub");
                    BtnPrimary.IsVisible = true;
                    BtnPrimary.Content = Loc.Get("btn_close");
                    BtnPrimary.IsEnabled = true;
                    BtnSecondary.IsVisible = false;
                    break;
                case Step.Error:
                    TxtStepTitle.Text = Loc.Get("label_local_ai_step_error");
                    TxtStepSubtitle.Text = Loc.Get("label_local_ai_step_error_sub");
                    BtnPrimary.IsVisible = true;
                    BtnPrimary.Content = Loc.Get("btn_retry");
                    BtnSecondary.Content = Loc.Get("btn_close");
                    BtnSecondary.IsVisible = true;
                    BtnPrimary.IsEnabled = true;
                    BtnSecondary.IsEnabled = true;
                    break;
            }
        }

        // -------- Step 1: Detect --------

        private async Task StartDetectAsync()
        {
            Show(Step.Detecting);
            _cts = new CancellationTokenSource();
            try
            {
                var snap = await _ollamaSetupService.DetectAsync(targetModel: _targetModel, ct: _cts.Token);

                switch (snap.Status)
                {
                    case OllamaInstallStatus.Ready:
                        // Already installed and model already pulled — nothing to do but the smoke test.
                        await StartSmokeTestAsync();
                        return;

                    case OllamaInstallStatus.RunningNoModel:
                        // Service is up; just need to pull the model.
                        await StartPullAsync();
                        return;

                    case OllamaInstallStatus.InstalledNotRunning:
                        // Bring the service up, then pull/smoke-test as appropriate.
                        var started = await _ollamaSetupService.StartServiceAsync(ct: _cts.Token);
                        if (!started)
                        {
                            ShowError(Loc.Get("error_local_ai_start_service_failed"));
                            return;
                        }
                        var snap2 = await _ollamaSetupService.DetectAsync(targetModel: _targetModel, ct: _cts.Token);
                        if (snap2.TargetModelInstalled) await StartSmokeTestAsync();
                        else await StartPullAsync();
                        return;

                    case OllamaInstallStatus.NotInstalled:
                    default:
                        Show(Step.Consent);
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LocalAiSetupWizard: detect failed");
                Show(Step.Consent);
            }
        }

        // -------- Step 2: Consent → Download --------

        private async Task StartDownloadInstallerAsync()
        {
            Show(Step.DownloadInstaller);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var progress = new Progress<OllamaDownloadProgress>(p =>
            {
                if (p.PercentComplete.HasValue)
                {
                    SetDownloadProgressBar(p.PercentComplete.Value);
                }

                var rate = _ollamaSetupService.FormatRate(p.BytesPerSecond);
                var bytes = _ollamaSetupService.FormatBytes(p.BytesReceived);
                if (p.TotalBytes.HasValue)
                {
                    var total = _ollamaSetupService.FormatBytes(p.TotalBytes.Value);
                    var pct = p.PercentComplete.HasValue ? $" ({p.PercentComplete.Value:0}%)" : "";
                    TxtDownloadProgress.Text = string.IsNullOrEmpty(rate)
                        ? $"{bytes} / {total}{pct}"
                        : $"{bytes} / {total}{pct} • {rate}";
                }
                else
                {
                    TxtDownloadProgress.Text = string.IsNullOrEmpty(rate) ? bytes : $"{bytes} • {rate}";
                }
            });

            try
            {
                var path = await _ollamaSetupService.DownloadInstallerAsync(progress, _cts.Token);
                await StartInstallAsync(path);
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LocalAiSetupWizard: installer download failed");
                ShowError(Loc.GetF("error_local_ai_download_failed", ex.Message));
            }
        }

        private void SetDownloadProgressBar(double percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            if (DownloadProgressFill.Parent is Grid g && g.Parent is Border border)
            {
                double max = Math.Max(0, border.Bounds.Width - 6);
                if (max > 0) DownloadProgressFill.Width = max * percent / 100.0;
            }
        }

        // -------- Step 3: Install --------

        private async Task StartInstallAsync(string installerPath)
        {
            Show(Step.Installing);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var ok = await _ollamaSetupService.RunInstallerSilentAsync(installerPath, ct: _cts.Token);
                if (!ok)
                {
                    // Leave the installer in %TEMP% on failure so the user (or a re-run)
                    // can inspect/retry without a fresh ~700MB download.
                    ShowError(Loc.Get("error_local_ai_install_failed"));
                    return;
                }
                // Drop the installer on success — it's ~700MB and unneeded once Ollama is in.
                try { if (global::System.IO.File.Exists(installerPath)) global::System.IO.File.Delete(installerPath); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete OllamaSetup.exe after install"); }
                await StartPullAsync();
            }
            catch (OperationCanceledException)
            {
                // Installing step disables cancel, but be defensive.
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LocalAiSetupWizard: silent install failed");
                ShowError(Loc.GetF("error_local_ai_install_failed_detail", ex.Message));
            }
        }

        // -------- Step 4: Pull model --------

        private async Task StartPullAsync()
        {
            Show(Step.PullModel);
            TxtPullHeader.Text = Loc.GetF("label_local_ai_pulling_model_named", _targetModel);
            TxtPullStatus.Text = "";
            TxtPullDetail.Text = "";
            SetPullProgressBar(0);

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var progress = new Progress<OllamaPullProgress>(p =>
            {
                TxtPullStatus.Text = p.Status;
                if (p.PercentComplete.HasValue)
                {
                    SetPullProgressBar(p.PercentComplete.Value);
                    var bytes = p.Completed.HasValue ? _ollamaSetupService.FormatBytes(p.Completed.Value) : "";
                    var total = p.Total.HasValue ? _ollamaSetupService.FormatBytes(p.Total.Value) : "";
                    TxtPullDetail.Text = string.IsNullOrEmpty(bytes)
                        ? $"{p.PercentComplete.Value:0}%"
                        : $"{bytes} / {total} ({p.PercentComplete.Value:0}%)";
                }
                else
                {
                    TxtPullDetail.Text = "";
                }
            });

            try
            {
                await _ollamaSetupService.PullModelAsync(_targetModel, progress: progress, ct: _cts.Token);
                SetPullProgressBar(100);
                await StartSmokeTestAsync();
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LocalAiSetupWizard: pull failed (model={Model})", _targetModel);
                ShowError(Loc.GetF("error_local_ai_pull_failed", _targetModel, ex.Message));
            }
        }

        private void SetPullProgressBar(double percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            if (PullProgressFill.Parent is Grid g && g.Parent is Border border)
            {
                double max = Math.Max(0, border.Bounds.Width - 6);
                if (max > 0) PullProgressFill.Width = max * percent / 100.0;
            }
        }

        // -------- Step 5: Smoke test --------

        private async Task StartSmokeTestAsync()
        {
            Show(Step.SmokeTest);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var (ok, elapsed, _) = await _ollamaSetupService.SmokeTestAsync(_targetModel, ct: _cts.Token);
                if (!ok)
                {
                    ShowError(Loc.Get("error_local_ai_smoke_failed"));
                    return;
                }

                Finish(elapsed);
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LocalAiSetupWizard: smoke test threw");
                ShowError(Loc.GetF("error_local_ai_smoke_threw", ex.Message));
            }
        }

        // -------- Step 6: Done --------

        private void Finish(TimeSpan smokeElapsed)
        {
            try
            {
                var prompt = _settingsService.Current?.CompanionPrompt;
                if (prompt != null)
                {
                    prompt.AiProvider = AiProviderType.Local;
                    prompt.AiModel = _targetModel;
                    _settingsService.Save();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LocalAiSetupWizard: failed to save settings on Finish");
            }

            LocalAiReady = true;
            _wizardComplete = true;

            var seconds = Math.Max(0, (int)Math.Round(smokeElapsed.TotalSeconds));
            TxtDoneDetail.Text = Loc.GetF("label_local_ai_done_detail", _targetModel, seconds);
            Show(Step.Done);
        }

        private void ShowError(string detail)
        {
            TxtErrorDetail.Text = detail;
            Show(Step.Error);
        }

        // -------- Footer button handlers --------

        private async void BtnPrimary_Click(object? sender, RoutedEventArgs e)
        {
            switch (_step)
            {
                case Step.Consent:
                    if (ChkAdvanced.IsChecked == true)
                    {
                        var typed = (TxtAdvancedModel.Text ?? "").Trim();
                        if (!string.IsNullOrEmpty(typed)) _targetModel = typed;
                    }
                    await StartDownloadInstallerAsync();
                    break;
                case Step.Done:
                    Result = true;
                    Close(Result);
                    break;
                case Step.Error:
                    // Retry from detect — the right next step depends on what's now true.
                    await StartDetectAsync();
                    break;
            }
        }

        private void BtnSecondary_Click(object? sender, RoutedEventArgs e)
        {
            // Cancel current step and bail out. The Done state hides this button entirely.
            _cts?.Cancel();
            Result = _wizardComplete;
            Close(Result);
        }

        private void ChkAdvanced_Changed(object? sender, RoutedEventArgs e)
        {
            AdvancedPanel.IsVisible = ChkAdvanced.IsChecked == true;
        }

        private void LinkManualInstall_Click(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ManualInstallUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LocalAiSetupWizard: failed to open manual install URL");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}
