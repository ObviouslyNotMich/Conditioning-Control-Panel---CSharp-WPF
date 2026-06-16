using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Tab navigation: tab-switching logic and content-control visibility management.
    public partial class MainWindow
    {
        #region Tab Navigation

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("settings");
        }

        private void BtnPresets_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("presets");
            RefreshPresetsList();
        }

        // BtnProgression handler removed in velvet-mosaic phase 6 — the Progression
        // tab no longer has a header button; its features live on the Dashboard now.

        private void BtnQuests_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("quests");
        }

        private void BtnEnhancements_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("enhancements");
        }

        private void BtnDeeper_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("deeper");
            if (App.Settings?.Current is { } s && !s.HasSeenDeeperTab)
            {
                s.HasSeenDeeperTab = true;
                StopDeeperTabPulse();
                App.Settings?.Save();
            }
            UpdateDeeperWelcomeCardVisibility();
            // Mission 2: lazy-init the hub the first time the user opens the
            // tab, then on every show pull a fresh scan. Cheap; doesn't churn
            // if the library hasn't changed.
            InitializeDeeperHub();
            ReloadDeeperLibraryFromDisk();
        }

        private void UpdateDeeperWelcomeCardVisibility()
        {
            if (DeeperWelcomeCard == null) return;
            var seen = App.Settings?.Current?.HasSeenDeeperWelcome ?? true;
            DeeperWelcomeCard.Visibility = seen ? Visibility.Collapsed : Visibility.Visible;
        }

        private void DismissDeeperWelcomeCard()
        {
            if (App.Settings?.Current is { } s && !s.HasSeenDeeperWelcome)
            {
                s.HasSeenDeeperWelcome = true;
                App.Settings?.Save();
            }
            if (DeeperWelcomeCard != null) DeeperWelcomeCard.Visibility = Visibility.Collapsed;
        }

        private void BtnDeeperWelcomeTour_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_tour"); } catch { }
            DismissDeeperWelcomeCard();
            StartDeeperTabTutorial();
        }

        private void BtnDeeperWelcomeDemo_Click(object sender, RoutedEventArgs e)
        {
            DismissDeeperWelcomeCard();
            OpenDeeperBundledDemo();
        }

        private void BtnDeeperWelcomeDismiss_Click(object sender, RoutedEventArgs e)
        {
            DismissDeeperWelcomeCard();
        }

        private void BtnDeeperTutorial_Click(object sender, RoutedEventArgs e)
        {
            StartDeeperTabTutorial();
        }

        // The bundled "Welcome to Deeper" demo is seeded into the user's library
        // on first run. Match by the literal filename rather than a hardcoded
        // path so we follow the user's library folder if they moved it.
        private void OpenDeeperBundledDemo()
        {
            try
            {
                var lib = App.EnhancementLibrary;
                if (lib == null) return;
                var match = lib.ScanLibrary()
                    .FirstOrDefault(e =>
                        string.Equals(System.IO.Path.GetFileName(e.FilePath), "welcome.ccpenh.json",
                            StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    MessageBox.Show(this,
                        "The bundled demo couldn't be found in your library — try restarting the app.",
                        "Deeper", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                OpenDeeperFile(match.FilePath);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Open bundled Deeper demo failed: {Error}", ex.Message);
            }
        }

        private void StartDeeperTabTutorial()
        {
            ShowTab("deeper");
            UpdateDeeperWelcomeCardVisibility(); // keep the card consistent with state
            StartTutorial(TutorialType.Deeper);
        }

        private void ChkEnableDeeper_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var enabled = ChkEnableDeeper.IsChecked ?? true;
            if (App.Settings?.Current is { } s) s.EnableDeeper = enabled;
            if (BtnDeeper != null) BtnDeeper.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            // If the user just disabled Deeper while it's the active tab, fall back to Settings.
            if (!enabled && DeeperTab?.Visibility == Visibility.Visible) ShowTab("settings");
            App.Settings?.Save();
        }

        private bool _deeperPulseRunning;

        private void StartDeeperTabPulse()
        {
            if (BtnDeeperScale == null || _deeperPulseRunning) return;
            _deeperPulseRunning = true;
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 1.12,
                Duration = TimeSpan.FromMilliseconds(700),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(4),
                EasingFunction = new System.Windows.Media.Animation.SineEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
                }
            };
            anim.Completed += (_, _) =>
            {
                _deeperPulseRunning = false;
                if (BtnDeeperScale != null)
                {
                    BtnDeeperScale.ScaleX = 1.0;
                    BtnDeeperScale.ScaleY = 1.0;
                }
            };
            BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
            BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, anim);
        }

        private void StopDeeperTabPulse()
        {
            if (!_deeperPulseRunning && BtnDeeperScale == null) return;
            _deeperPulseRunning = false;
            if (BtnDeeperScale != null)
            {
                BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
                BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
                BtnDeeperScale.ScaleX = 1.0;
                BtnDeeperScale.ScaleY = 1.0;
            }
        }

        private void BtnDeeperNewEnhancement_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_new"); } catch { }
            var dialog = new Views.Deeper.NewEnhancementDialog { Owner = this };
            if (dialog.ShowDialog() != true) return;

            var enhancement = App.EnhancementLibrary?.CreateBlank(dialog.SelectedMediaType, dialog.SelectedSource);
            if (enhancement == null) return;

            OpenDeeperEditor(enhancement, null);
        }

        private void BtnDeeperOpenPlayer_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_player"); } catch { }
            try
            {
                var win = new Views.Deeper.EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost) { Owner = this };
                win.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player");
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeeperBrowserBound(string pageUrl, Models.Deeper.Enhancement enhancement)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    DeeperBrowserBadge.Visibility = Visibility.Visible;
                    var name = string.IsNullOrEmpty(enhancement.Metadata?.Name) ? "(untitled)" : enhancement.Metadata!.Name;
                    TxtDeeperBrowserBadge.Text = $"🌊 {name}";
                    DeeperBrowserBadge.Tag = $"{name}\n{pageUrl}";

                    // QoL: if the bound enhancement uses webcam-driven rules and
                    // tracking isn't already running, ask the user once whether
                    // they want to turn it on. Mirrors the player's behavior so
                    // gaze/blink/attention rules actually fire in the browser.
                    MaybePromptBrowserWebcamForEnhancement(enhancement);
                }
                catch { }
            });
        }

        private void OnDeeperBrowserUnbound()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    DeeperBrowserBadge.Visibility = Visibility.Collapsed;
                    DeeperBrowserBadge.Tag = null;
                    _browserWebcamPromptShownForUrl = null;
                }
                catch { }
            });
        }

        // ────────────────────────────────────────────────────────────────────
        // Browser Webcam Tracking toggle (button above the embedded WebView2)
        // ────────────────────────────────────────────────────────────────────

        private bool _browserWebcamStateSubscribed;
        private Action<WebcamTrackingState>? _onBrowserWebcamStateChanged;
        // Tracks the page URL we've already prompted about so reload-binds
        // don't badger the user repeatedly for the same enhancement.
        private string? _browserWebcamPromptShownForUrl;

        private void EnsureBrowserWebcamStateSubscribed()
        {
            if (_browserWebcamStateSubscribed || App.Webcam == null) return;
            _browserWebcamStateSubscribed = true;
            _onBrowserWebcamStateChanged = _ => Dispatcher.BeginInvoke(RefreshBrowserWebcamButton);
            App.Webcam.OnTrackingStateChanged += _onBrowserWebcamStateChanged;
            RefreshBrowserWebcamButton();
        }

        private void RefreshBrowserWebcamButton()
        {
            try
            {
                if (BtnWebcamTracking == null) return;
                var on = App.Webcam?.IsRunning == true;
                if (TxtWebcamTracking != null)
                    TxtWebcamTracking.Text = Loc.Get(on
                        ? "btn_browser_webcam_tracking_on"
                        : "btn_browser_webcam_tracking_off");
                BtnWebcamTracking.ToolTip = Loc.Get(on
                    ? "tooltip_browser_webcam_tracking_on"
                    : "tooltip_browser_webcam_tracking_off");
            }
            catch { }
        }

        private async void BtnWebcamTracking_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("webcam_tracking"); } catch { }
            var svc = App.Webcam;
            if (svc == null) return;

            if (svc.IsRunning)
            {
                svc.Stop();
                RefreshBrowserWebcamButton();
                return;
            }

            // Consent gate. If declined or cancelled, bail silently — user can
            // try again from this same button or from the Lab tab later.
            if (!WebcamTrackingService.IsConsentCurrent())
            {
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven) return;
            }

            // Needs-calibration path: the calibration window reads OnRawIris,
            // which only fires while the tracker is running. So we start the
            // camera FIRST, then open calibration on top of the live stream.
            // Tell the user up front so the camera light surprising them
            // doesn't feel like the app did something it shouldn't have.
            bool needsCalibration = svc.Calibration == null;
            if (needsCalibration)
            {
                var confirm = MessageBox.Show(this,
                    Loc.Get("browser_webcam_calibrate_prompt_body"),
                    Loc.Get("browser_webcam_calibrate_prompt_title"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (confirm != MessageBoxResult.OK) return;
            }

            // Start off the UI thread — Start() does VideoCapture open + ONNX
            // session ctors and can block 10-30s on slow USB negotiation.
            bool started;
            try
            {
                started = await Task.Run(() => svc.Start());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Browser webcam toggle: Start() threw");
                started = false;
            }
            RefreshBrowserWebcamButton();

            if (!started)
            {
                App.Logger?.Warning("Browser webcam toggle: Start() returned false, state={State}", svc.State);
                return;
            }

            // Now the tracker is live — run the 16-point calibration. If the
            // user cancels we stop the tracker again so the camera light
            // doesn't stay on for a feature they backed out of.
            if (needsCalibration)
            {
                var calibrated = WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                if (calibrated != true)
                {
                    svc.Stop();
                    RefreshBrowserWebcamButton();
                }
            }
        }

        // Webcam-needing enhancement check. Delegates to the shared
        // EnhancementCapabilities.NeedsWebcam so the browser hub, the mandatory-
        // video engine-start nudge, and the Deeper player all answer identically
        // (AutoTags first, then a trigger scan across both the unified timeline
        // and the legacy Rules collection).
        private static bool BrowserEnhancementNeedsWebcam(Models.Deeper.Enhancement enh)
            => Services.Deeper.EnhancementCapabilities.NeedsWebcam(enh);

        private void MaybePromptBrowserWebcamForEnhancement(Models.Deeper.Enhancement enhancement)
        {
            try
            {
                if (enhancement == null) return;
                if (!BrowserEnhancementNeedsWebcam(enhancement)) return;
                var svc = App.Webcam;
                if (svc == null || svc.IsRunning) return;

                // Dedupe per-page so reload/dom-mutation doesn't re-pop the dialog.
                var url = App.DeeperBrowserDiscovery?.ActiveUrl ?? "";
                if (!string.IsNullOrEmpty(_browserWebcamPromptShownForUrl) &&
                    string.Equals(_browserWebcamPromptShownForUrl, url, StringComparison.OrdinalIgnoreCase))
                    return;
                _browserWebcamPromptShownForUrl = url;

                var result = MessageBox.Show(this,
                    Loc.Get("browser_webcam_prompt_body"),
                    Loc.Get("browser_webcam_prompt_title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                // Reuse the toggle button's flow so consent + calibration
                // gating is identical to the manual-click path.
                BtnWebcamTracking_Click(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MaybePromptBrowserWebcamForEnhancement: {Error}", ex.Message);
            }
        }

        // Set once the engine-start enhancement nudge has been shown this launch,
        // so repeated Start/Stop cycles don't re-pop it (once per launch; a "Not
        // now" is remembered until the app restarts — webcam isn't auto-started,
        // so a fresh launch legitimately re-asks).
        private bool _mandatoryVideoEnhanceNudgeShown;

        /// <summary>
        /// Engine-start nudge: if the mandatory / asset video folder contains an
        /// enhanced video the current settings won't fully honour, offer to flip
        /// the missing switch(es) in one combined dialog:
        ///   • VideoEnhanceIfPossible is off but enhanced videos exist → enable it.
        ///   • An enhancement needs the webcam tracker but it isn't running → start
        ///     it, routing through the same consent/calibration flow as the manual
        ///     toggle.
        /// Scans the folder off the UI thread (cached + short-circuited) and
        /// prompts at most once per launch. No-op unless mandatory videos are on.
        /// </summary>
        private async void MaybePromptMandatoryVideoEnhancement()
        {
            try
            {
                if (_mandatoryVideoEnhanceNudgeShown) return;

                var settings = App.Settings?.Current;
                if (settings == null || !settings.MandatoryVideosEnabled) return;

                // Don't interrupt remote-controlled or locked-down sessions.
                if (App.RemoteControl?.ControllerConnected == true) return;
                if (App.Lockdown?.IsActive == true) return;

                var folder = System.IO.Path.Combine(App.EffectiveAssetsPath, "videos");

                Services.Deeper.MandatoryVideoEnhancementScanner.ScanResult scan;
                try
                {
                    scan = await Task.Run(() => Services.Deeper.MandatoryVideoEnhancementScanner.Scan(folder));
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("MaybePromptMandatoryVideoEnhancement: scan failed: {Error}", ex.Message);
                    return;
                }

                // The window/engine may have gone away during the async scan.
                if (!IsLoaded || Dispatcher.HasShutdownStarted) return;
                if (!_isRunning) return;            // engine stopped before scan returned
                if (_mandatoryVideoEnhanceNudgeShown) return; // a concurrent start won the race

                if (!scan.AnyEnhanced) return;      // nothing to nudge about

                var enhanceOff = !settings.VideoEnhanceIfPossible;
                var webcamSvc = App.Webcam;
                var webcamGap = scan.AnyWebcamEnhanced && (webcamSvc == null || !webcamSvc.IsRunning);

                // No gap → no prompt (enhancement already on and either no webcam
                // rules or the webcam is already tracking).
                if (!enhanceOff && !webcamGap) return;

                _mandatoryVideoEnhanceNudgeShown = true;

                var sb = new System.Text.StringBuilder();
                sb.Append("Some videos in your mandatory video folder have enhancements ");
                sb.Append("(synced flashes, haptics, overlays and more).\n\n");
                if (enhanceOff)
                    sb.Append("• Video enhancement is currently turned OFF, so they won't play.\n");
                if (webcamGap)
                    sb.Append("• Some use webcam tracking (gaze / blink), but the webcam engine isn't running.\n");
                sb.Append("\nWould you like to turn ");
                sb.Append(enhanceOff && webcamGap ? "these on now?"
                          : enhanceOff ? "enhancement on now?"
                          : "the webcam on now?");

                var yes = enhanceOff && webcamGap ? "Yes, set it up"
                          : enhanceOff ? "Yes, enable enhancement"
                          : "Yes, turn on webcam";

                var confirmed = ShowStyledDialog("✨ Enhanced videos detected", sb.ToString(), yes, "Not now");
                if (!confirmed) return;

                if (enhanceOff)
                {
                    settings.VideoEnhanceIfPossible = true;
                    App.Settings?.Save();
                    App.Logger?.Information("Mandatory-video enhancement enabled via engine-start nudge.");
                }

                if (webcamGap)
                {
                    // Reuse the manual toggle's flow so consent + calibration
                    // gating is identical to clicking the webcam button.
                    BtnWebcamTracking_Click(this, new RoutedEventArgs());
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MaybePromptMandatoryVideoEnhancement: {Error}", ex.Message);
            }
        }

        private void ToggleEnhanceIfPossible_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var newValue = ToggleEnhanceIfPossible?.IsChecked == true;
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.BrowserEnhanceIfPossible = newValue;
                    App.Settings.Save();
                }
                App.BrowserEnhanceBridge?.Refresh();

                // If just turned off, status text needs an immediate reset since
                // Refresh() will fire MatchChanged(null) but we want to be explicit.
                if (!newValue && TxtEnhanceMatchStatus != null)
                    TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_match_off");
            }
            catch (Exception ex) { App.Logger?.Debug("ToggleEnhanceIfPossible_Changed: {Error}", ex.Message); }
        }

        private void OnBrowserEnhanceMatchChanged(Services.Deeper.EnhancementLibraryEntry? match)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (TxtEnhanceMatchStatus == null) return;
                    if (App.Settings?.Current?.BrowserEnhanceIfPossible == false)
                    {
                        TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_match_off");
                        return;
                    }
                    if (match == null)
                    {
                        TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_match_none");
                        return;
                    }
                    var name = string.IsNullOrEmpty(match.Name) ? "(untitled)" : match.Name;
                    TxtEnhanceMatchStatus.Text = string.Format(Loc.Get("browser_enhance_match_fmt"), name);
                }
                catch { }
            });
        }

        private void OpenDeeperEditor(Models.Deeper.Enhancement enhancement, string? filePath)
        {
            try
            {
                var window = new Views.Deeper.DeeperEditorWindow(enhancement, filePath) { Owner = this };
                window.Closed += (_, _) => RefreshDeeperLibraryUI();
                window.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper editor");
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---- Public entry points for "Open with CCP" + drag-drop dispatch ----

        public void OpenInDeeperPlayer(string mediaPath)
        {
            try
            {
                var win = new Views.Deeper.EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost) { Owner = this };
                win.Show();
                win.OpenLocalMediaFile(mediaPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player for {Path}", mediaPath);
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Opens the Player and loads a Deeper enhancement JSON. The host fires
        // its Loaded event which routes to OnHostLoaded → UpdateHostUi → the
        // correct media loader (remote URL / local video / audio) based on the
        // enhancement's MediaType + MediaSource. Used by the hub row's ▶
        // button — distinct from OpenInDeeperPlayer (which takes a media path).
        // Mission 3: opens the editor for an existing .ccpenh.json file. Used
        // by the Player's "Open in editor" jump (header button + event log
        // link). Routes through OpenDeeperFile so the same load/error path
        // and library-refresh-on-close behavior applies as the hub row click.
        public void OpenDeeperEditorFromPlayer(string ccpenhJsonPath)
        {
            if (string.IsNullOrWhiteSpace(ccpenhJsonPath)) return;
            OpenDeeperFile(ccpenhJsonPath);
        }

        public void OpenDeeperEnhancementInPlayer(string ccpenhJsonPath)
        {
            try
            {
                var win = new Views.Deeper.EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost) { Owner = this };
                win.Show();
                win.LoadEnhancementFile(ccpenhJsonPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player for enhancement {Path}", ccpenhJsonPath);
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenInDeeperEditorForMedia(string mediaPath)
        {
            try
            {
                var ext = Path.GetExtension(mediaPath);
                var mediaType = AssetVideoExtensions.Contains(ext)
                    ? Models.Deeper.MediaTypes.Video
                    : Models.Deeper.MediaTypes.Audio;
                var enhancement = App.EnhancementLibrary?.CreateBlank(mediaType, mediaPath);
                if (enhancement == null) return;
                OpenDeeperEditor(enhancement, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper editor for {Path}", mediaPath);
                MessageBox.Show(this,
                    $"Couldn't open Deeper Editor:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Editor failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void HandlePendingFileOpen(string action, string path)
        {
            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(path)) return;
            if (action == "play") OpenInDeeperPlayer(path);
            else if (action == "edit") OpenInDeeperEditorForMedia(path);
        }

        private void OnDeeperLibraryChanged(object? sender, EventArgs e)
        {
            // Library change events arrive on the dispatcher thread already
            // (EnhancementLibrary marshals via Application.Current.Dispatcher),
            // but only refresh if the tab is actually visible — a hidden tab
            // would just throw the work away on the next ShowTab.
            try
            {
                if (DeeperTab?.Visibility == Visibility.Visible)
                    RefreshDeeperLibraryUI();
            }
            catch (Exception ex) { App.Logger?.Debug("Deeper library refresh error: {Error}", ex.Message); }
        }

        // Mission 2 (hub redesign): the old StackPanel-rebuild was replaced by
        // the ObservableCollection + DataTemplate path in MainWindow.DeeperHub.cs.
        // Kept as a stable hook so the ~5 call sites (OpenDeeperEditor's Closed
        // handler, DeleteDeeperLibraryEntry, OnDeeperLibraryChanged, etc.) don't
        // need touching.
        private void RefreshDeeperLibraryUI() => ReloadDeeperLibraryFromDisk();

        // Mission 2: BuildDeeperLibraryRow / BuildDeeperRecentRow / BuildDeeperAutoTagsRow
        // / BuildDeeperMediaLine deleted with the two-column Library + Recent grid.
        // Their visual roles moved into the DeeperLibraryRowTemplate DataTemplate
        // in MainWindow.xaml (Mission 2 commit). VM construction lives in
        // MainWindow.DeeperHub.cs:BuildRowVm + ResolveMediaSourceDisplay.

        private void OpenDeeperFile(string path)
        {
            try
            {
                var enhancement = App.EnhancementLibrary?.Open(path);
                if (enhancement == null) return;
                OpenDeeperEditor(enhancement, path);
            }
            catch (Services.Deeper.EnhancementLoadException ex)
            {
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper file {Path}", path);
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnDeeperOpenLibraryFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = App.EnhancementLibrary?.LibraryFolder;
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper library folder {Folder}", folder);
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnDeeperImport_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_import"); } catch { }
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import enhancement",
                Filter = "Deeper enhancements (*.ccpenh.json)|*.ccpenh.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".ccpenh.json",
                Multiselect = true,
                CheckFileExists = true,
                InitialDirectory = App.EnhancementLibrary?.LastDirectory
            };
            if (dlg.ShowDialog(this) != true) return;
            ImportEnhancementFiles(dlg.FileNames);
        }

        // NOTE: Deeper-tab drag-and-drop is intentionally handled by the window-wide
        // Window_Drop / DetectDropType system (DropType.Enhancement → ImportEnhancementFiles)
        // rather than a tab-local handler. A tab-local AllowDrop handler swallowed the
        // bubbling drag events the global drop overlay relies on, so it only covered
        // part of the tab. Keeping one global handler makes the entire window — and thus
        // the whole Deeper tab — a uniform drop target.

        private static bool IsImportableEnhancementPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            // Accept either the canonical "*.ccpenh.json" double-suffix or a plain
            // ".json" — the serializer will reject anything that doesn't carry the
            // expected $schema tag, so plain .json is safe to offer.
            return path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private void ImportEnhancementFiles(System.Collections.Generic.IEnumerable<string> paths)
        {
            var lib = App.EnhancementLibrary;
            if (lib == null)
            {
                MessageBox.Show(this, "Enhancement library isn't ready yet.", "Import failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var imported = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<string>();
            string? lastImportedPath = null;

            foreach (var path in paths)
            {
                if (!IsImportableEnhancementPath(path))
                {
                    errors.Add($"{System.IO.Path.GetFileName(path)} — not a .ccpenh.json file.");
                    continue;
                }
                try
                {
                    // Validate by loading; bad schema / oversized files throw with
                    // a useful message from the serializer.
                    var enhancement = Services.Deeper.EnhancementSerializer.LoadFromFile(path);
                    var saved = lib.PromoteToLibrary(enhancement, sourceTag: "import");
                    if (saved == null)
                    {
                        errors.Add($"{System.IO.Path.GetFileName(path)} — couldn't write into the library folder.");
                        continue;
                    }
                    lastImportedPath = saved;
                    imported.Add(System.IO.Path.GetFileName(saved));
                }
                catch (Services.Deeper.EnhancementLoadException ex)
                {
                    errors.Add($"{System.IO.Path.GetFileName(path)} — {ex.Message}");
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "ImportEnhancementFiles: failed on {Path}", path);
                    errors.Add($"{System.IO.Path.GetFileName(path)} — {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Remember the source folder for next manual import so the file dialog
            // opens where the user picked from.
            if (lastImportedPath != null && App.Settings?.Current != null)
            {
                var src = paths.FirstOrDefault();
                if (!string.IsNullOrEmpty(src))
                {
                    var dir = System.IO.Path.GetDirectoryName(src);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        App.Settings.Current.DeeperLastDirectory = dir;
                        App.Settings.Save();
                    }
                }
            }

            // Force-refresh the hub list now in addition to the FileSystemWatcher
            // signal, since the watcher's debounce can lag a fast manual import.
            try { RefreshDeeperLibraryUI(); } catch { }

            if (errors.Count == 0 && imported.Count > 0)
            {
                var msg = imported.Count == 1
                    ? $"Imported \"{imported[0]}\" into your library."
                    : $"Imported {imported.Count} enhancements into your library.";
                MessageBox.Show(this, msg, "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (errors.Count > 0)
            {
                var head = imported.Count > 0
                    ? $"Imported {imported.Count}, but {errors.Count} failed:\n\n"
                    : $"{errors.Count} file(s) couldn't be imported:\n\n";
                MessageBox.Show(this, head + string.Join("\n", errors), "Import failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteDeeperLibraryEntry(Services.Deeper.EnhancementLibraryEntry entry)
        {
            var label = string.IsNullOrEmpty(entry.Name) ? System.IO.Path.GetFileName(entry.FilePath) : entry.Name;
            var msg = string.Format(Loc.Get("deeper_library_delete_confirm_fmt"), label);
            var result = MessageBox.Show(this, msg, Loc.Get("deeper_library_delete_title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;
            try
            {
                if (System.IO.File.Exists(entry.FilePath))
                    System.IO.File.Delete(entry.FilePath);
                // FileSystemWatcher in EnhancementLibrary will fire LibraryChanged
                // and refresh the UI, but force an immediate refresh for snappiness.
                RefreshDeeperLibraryUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to delete Deeper library entry {Path}", entry.FilePath);
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // W3 Piece 1 — invoked from BrowserService.NavigationCompleted on every
        // page load in the embedded browser. Filters non-HT URLs out, debounces
        // rapid navigations via a per-window CTS, and surfaces the result as a
        // toast (or no toast at all when there are no results / lookup failed).
        //
        // Runs entirely off the WebView2 navigation thread context because the
        // caller already marshals through Dispatcher.Invoke before calling us,
        // but the lookup itself awaits on the thread pool so we don't block UI
        // during the network call.
        private void TriggerCatalogueLookupForNavigation(string url)
        {
            try
            {
                if (App.CatalogueLookup == null) return;
                // Cheap pre-filter — saves the cost of starting a Task for the
                // (very common) case of a non-HT navigation. The service does
                // the same check defensively.
                if (!Helpers.HtUrlHelper.IsEligibleHtUrl(url)) return;

                // Cancel any in-flight lookup from the previous navigation so a
                // delayed response doesn't surface a toast for a page the user
                // has already left.
                try { _catalogueLookupCts?.Cancel(); }
                catch { /* idempotent */ }
                _catalogueLookupCts?.Dispose();
                var cts = new System.Threading.CancellationTokenSource();
                _catalogueLookupCts = cts;

                _ = RunCatalogueLookupAsync(url, cts.Token);
            }
            catch (Exception ex)
            {
                // Defensive — must never propagate out of a navigation event handler.
                App.Logger?.Warning(ex, "[Catalogue] TriggerCatalogueLookupForNavigation threw");
            }
        }

        private async System.Threading.Tasks.Task RunCatalogueLookupAsync(string url, System.Threading.CancellationToken ct)
        {
            LookupResult result;
            try
            {
                result = await App.CatalogueLookup!.LookupForUrlAsync(url, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return; // user navigated away; nothing to do
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Lookup threw unexpectedly");
                return;
            }

            // Drop the result silently if a newer navigation has since started
            // — protects against the (small) race window between the lookup
            // returning and the CTS getting cancelled.
            if (ct.IsCancellationRequested) return;

            switch (result)
            {
                case LookupResult.Success s:
                    ShowCatalogueLookupToast(url, s.Entries);
                    break;
                case LookupResult.None:
                case LookupResult.InvalidUrl:
                case LookupResult.NetworkError:
                    // No user-visible feedback on these — silent by design.
                    break;
            }
        }

        // Surface a toast for {N} discovered enhancements. Updates
        // _currentCatalogueHtVideoId so the action handler can validate the
        // user is still on the same video when they click.
        private void ShowCatalogueLookupToast(string url, System.Collections.Generic.List<CatalogueEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;

            var videoId = Helpers.HtUrlHelper.TryExtractHtVideoId(url);
            _currentCatalogueHtVideoId = videoId;

            string message;
            string actionLabel;
            if (entries.Count == 1)
            {
                message = Loc.Get("catalogue_lookup_toast_one");
                actionLabel = Loc.Get("catalogue_lookup_action_use_one");
            }
            else
            {
                message = string.Format(Loc.Get("catalogue_lookup_toast_many_fmt"), entries.Count);
                actionLabel = Loc.Get("catalogue_lookup_action_pick_one");
            }

            // Snapshot the entries + video ID for the action closure so a later
            // mutation of _currentCatalogueHtVideoId by a parallel navigation
            // can be detected.
            var snapshotEntries = entries;
            var snapshotVideoId = videoId;

            App.Notifications?.Show(message, NotificationType.Info, TimeSpan.FromSeconds(10),
                actionLabel,
                () =>
                {
                    // Stale-toast guard: the user navigated away before clicking.
                    // Silently bail — they'll see a fresh toast for whatever
                    // video they're now on (if any).
                    if (!string.Equals(_currentCatalogueHtVideoId, snapshotVideoId, StringComparison.Ordinal))
                    {
                        App.Logger?.Information("[Catalogue] Toast action ignored (user navigated away)");
                        return;
                    }

                    if (snapshotEntries.Count == 1)
                    {
                        _ = DownloadAndOpenCatalogueEntryAsync(snapshotEntries[0]);
                    }
                    else
                    {
                        OpenCataloguePickerDialog(snapshotEntries, snapshotVideoId);
                    }
                });
        }

        private void OpenCataloguePickerDialog(System.Collections.Generic.List<CatalogueEntry> entries, string? videoId)
        {
            try
            {
                var dlg = new CataloguePickerDialog(entries, videoId) { Owner = this };
                dlg.ShowDialog();
                if (dlg.SelectedEntry != null)
                {
                    _ = DownloadAndOpenCatalogueEntryAsync(dlg.SelectedEntry);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Picker dialog threw");
            }
        }

        private async System.Threading.Tasks.Task DownloadAndOpenCatalogueEntryAsync(CatalogueEntry entry)
        {
            DownloadResult result;
            try
            {
                // Pass default cancellation here — the per-navigation CTS is
                // about lookups, not downloads. Once the user has clicked
                // through, they expect the download to complete even if they
                // navigate the browser away while it's in flight.
                result = await App.CatalogueLookup!.DownloadAndOpenAsync(entry, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Download flow threw");
                App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_download_failed"),
                    NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            switch (result)
            {
                case DownloadResult.Success s:
                    App.Notifications?.Show(
                        string.Format(Loc.Get("catalogue_lookup_toast_loaded_fmt"), entry.Title),
                        NotificationType.Info, TimeSpan.FromSeconds(6));
                    break;
                case DownloadResult.NetworkError:
                    App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_download_failed"),
                        NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.InvalidFile:
                    App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_invalid_file"),
                        NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.SaveError:
                    App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_save_failed"),
                        NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.OpenError oe:
                    App.Notifications?.Show(
                        string.Format(Loc.Get("catalogue_lookup_toast_open_failed_fmt"), oe.LocalFilename),
                        NotificationType.Warning, TimeSpan.FromSeconds(10),
                        Loc.Get("catalogue_lookup_action_open_library"),
                        () => SwitchToDeeperLibraryTab());
                    break;
            }
        }

        // Focus the Deeper Library tab when the user clicks "Open Library"
        // from the OpenError recovery toast.
        private void SwitchToDeeperLibraryTab()
        {
            try { ShowTab("deeper"); }
            catch (Exception ex) { App.Logger?.Debug("[Catalogue] SwitchToDeeperLibraryTab failed: {Msg}", ex.Message); }
        }

        // Catalogue eligibility check — wraps the URL helper with the media-type
        // gate (audio enhancements aren't catalogued in W2).
        //
        // URL eligibility itself lives in Helpers/HtUrlHelper.cs because it's
        // now shared by three callers:
        //   1. This W2 row-level submit gate
        //   2. The catalogue server (kept in sync via cclabs-web's enhancements.ts)
        //   3. W3 Piece 1's CatalogueLookupService navigation hook
        // The two client consumers MUST agree on what counts as an HT URL —
        // see HtUrlHelper for the shared regex pair and update both this client
        // helper AND the server's normalizeHtUrl together when adding patterns.
        private static bool IsCatalogueEligible(Services.Deeper.EnhancementLibraryEntry entry)
        {
            if (entry == null) return false;
            if (entry.MediaType != Models.Deeper.MediaTypes.Video) return false;
            return Helpers.HtUrlHelper.IsEligibleHtUrl(entry.MediaSource);
        }

        // W2 — submit a library enhancement to the cclabs catalogue. Opens the
        // affirmation modal; on confirmation, awaits CatalogueService and maps
        // the SubmissionResult to a NotificationService toast per the spec.
        private async Task SubmitDeeperLibraryEntryAsync(Services.Deeper.EnhancementLibraryEntry entry)
        {
            // Defense in depth — the button is already disabled when auth is
            // missing, but a race (token expiring between row build and click)
            // would otherwise produce an AuthFailed toast as the first feedback.
            if (string.IsNullOrEmpty(App.Settings?.Current?.AuthToken))
            {
                App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                    Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                return;
            }

            var label = string.IsNullOrEmpty(entry.Name) ? System.IO.Path.GetFileName(entry.FilePath) : entry.Name;
            var dialog = new CatalogueSubmitDialog(label) { Owner = this };
            if (dialog.ShowDialog() != true || !dialog.Confirmed) return;

            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitEnhancementAsync(entry.FilePath, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // CatalogueService is designed to never throw, but defensively
                // surface anything that escapes as an UnknownError.
                App.Logger?.Warning(ex, "[Catalogue] Submit threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }

            // Remember the submission so the library badge + the eventual
            // "published" notification can track it (no-op for non-ack results).
            RecordDeeperSubmission(entry.FilePath, result);

            ShowCatalogueSubmissionResultToast(result);
        }

        // Map a SubmissionResult to a localized toast. Kept separate from the
        // submit method so tests / future flows (e.g. a "retry all failed"
        // button) can reuse the mapping.
        private void ShowCatalogueSubmissionResultToast(SubmissionResult result)
        {
            switch (result)
            {
                case SubmissionResult.Success:
                    // Success uses the distinct green border (#4CAF50) so the
                    // happy-path outcome is visually unambiguous next to the
                    // pink Info border that Duplicate uses.
                    App.Notifications?.Show(Loc.Get("catalogue_toast_success"),
                        Services.NotificationType.Success, TimeSpan.FromSeconds(6));
                    break;

                case SubmissionResult.Duplicate d:
                {
                    var key = d.ExistingStatus switch
                    {
                        "approved" => "catalogue_toast_duplicate_approved",
                        "rejected" => "catalogue_toast_duplicate_rejected",
                        _ => "catalogue_toast_duplicate_pending",
                    };
                    App.Notifications?.Show(Loc.Get(key),
                        Services.NotificationType.Info, TimeSpan.FromSeconds(6));
                    break;
                }

                case SubmissionResult.ValidationError v:
                {
                    var key = v.ErrorCode switch
                    {
                        "missing_title" => "catalogue_toast_error_missing_title",
                        "missing_creator" => "catalogue_toast_error_missing_creator",
                        "invalid_media_source" => "catalogue_toast_error_invalid_media_source",
                        "invalid_schema" => "catalogue_toast_error_invalid_schema",
                        "file_too_large" => "catalogue_toast_error_file_too_large",
                        "stale_guidelines_version" => "catalogue_toast_error_stale_guidelines",
                        _ => "",
                    };
                    var msg = !string.IsNullOrEmpty(key)
                        ? Loc.Get(key)
                        : Loc.GetF("catalogue_toast_error_generic_fmt", v.ErrorCode);
                    App.Notifications?.Show(msg, Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                    break;
                }

                case SubmissionResult.AuthFailed:
                    // CatalogueService.MapResponse already invalidated the
                    // cache for us; the user just needs to re-link in Settings.
                    App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                        Services.NotificationType.Warning, TimeSpan.FromSeconds(10));
                    break;

                case SubmissionResult.TooLarge:
                    App.Notifications?.Show(Loc.Get("catalogue_toast_too_large"),
                        Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;

                case SubmissionResult.RateLimited r:
                {
                    string msg;
                    if (r.RetryAfterSeconds.HasValue && r.RetryAfterSeconds.Value > 0)
                    {
                        var minutes = Math.Max(1, (int)Math.Ceiling(r.RetryAfterSeconds.Value / 60.0));
                        msg = Loc.GetF("catalogue_toast_rate_limited_minutes_fmt", minutes);
                    }
                    else
                    {
                        msg = Loc.Get("catalogue_toast_rate_limited_unknown");
                    }
                    App.Notifications?.Show(msg, Services.NotificationType.Warning, TimeSpan.FromSeconds(10));
                    break;
                }

                case SubmissionResult.UnknownError u:
                    App.Logger?.Warning("[Catalogue] Submission UnknownError status={Status} body={Body}",
                        u.StatusCode, u.Body);
                    App.Notifications?.Show(Loc.Get("catalogue_toast_unknown_error"),
                        Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
            }
        }

        private void BtnRerollDaily_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("reroll_daily"); } catch { }
            if (App.Quests?.RerollDailyQuest() == true)
            {
                RefreshQuestUI();
            }
            else
            {
                var hasPatreon = App.Patreon?.HasPremiumAccess == true;
                var msg = hasPatreon
                    ? "You've used all 3 daily rerolls! Rerolls reset at midnight."
                    : "You've used your daily reroll! Patreon supporters get 2 extra rerolls.";
                MessageBox.Show(msg, "Reroll Limit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRerollWeekly_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("reroll_weekly"); } catch { }
            if (App.Quests?.RerollWeeklyQuest() == true)
            {
                RefreshQuestUI();
            }
            else
            {
                var hasPatreon = App.Patreon?.HasPremiumAccess == true;
                var msg = hasPatreon
                    ? "You've used all 3 weekly rerolls! Rerolls reset on Sunday."
                    : "You've used your weekly reroll! Patreon supporters get 2 extra rerolls.";
                MessageBox.Show(msg, "Reroll Limit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }


        private void RefreshQuestUI()
        {
            var questService = App.Quests;
            if (questService == null) return;

            // Proactively recalculate streak from calendar so stale values are caught immediately
            questService.RecalculateStreak();

            // Update season title from server or defaults
            var seasonTitle = App.QuestDefinitions?.SeasonTitle;
            if (!string.IsNullOrEmpty(seasonTitle))
            {
                TxtSeasonTitle.Text = seasonTitle;
            }

            // Update daily quest counter badge
            int dailyCompleted = questService.GetDailyQuestsCompletedToday();
            TxtDailyQuestCounter.Text = $"{dailyCompleted}/{QuestService.MaxDailyQuestsPerDay}";
            bool allDailyDone = questService.AreAllDailyQuestsCompleted();

            // Update daily progress segments
            var goldBrush = _dailySegmentGold ??= new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
            var greyBrush = _dailySegmentGrey ??= new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));
            DailySegment1.Background = dailyCompleted >= 1 ? goldBrush : greyBrush;
            DailySegment2.Background = dailyCompleted >= 2 ? goldBrush : greyBrush;
            DailySegment3.Background = dailyCompleted >= 3 ? goldBrush : greyBrush;

            // Refresh daily quest display
            var dailyDef = questService.GetCurrentDailyDefinition();
            var dailyProgress = questService.Progress.DailyQuest;
            if (allDailyDone)
            {
                // All 3 daily quests completed - show the "all done" message
                DailyQuestCard.Visibility = Visibility.Collapsed;
                DailyAllCompletedMessage.Visibility = Visibility.Visible;
                BtnRerollDaily.Visibility = Visibility.Collapsed;
            }
            else if (dailyDef != null && dailyProgress != null)
            {
                DailyQuestCard.Visibility = Visibility.Visible;
                DailyAllCompletedMessage.Visibility = Visibility.Collapsed;
                BtnRerollDaily.Visibility = Visibility.Visible;

                TxtDailyQuestIcon.Text = dailyDef.Icon;
                TxtDailyQuestName.Text = App.Mods?.MakeModAware(dailyDef.Name) ?? dailyDef.Name;
                TxtDailyQuestDesc.Text = App.Mods?.MakeModAware(dailyDef.Description) ?? dailyDef.Description;
                TxtDailyProgress.Text = $"{dailyProgress.CurrentProgress} / {dailyDef.TargetValue}";
                // Show scaled XP based on level (+4% per level), reroll bonus, and streak bonus
                var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                var rerollMult = App.SkillTree?.GetRerollBonusMultiplier() ?? 1.0;
                var questStreak = App.Settings?.Current?.DailyQuestStreak ?? 0;
                var streakMult = 1.0 + (questStreak * 0.03);
                var scaledDailyXP = (int)Math.Round(dailyDef.XPReward * (1 + playerLevel * 0.04) * rerollMult * streakMult);
                TxtDailyXP.Text = $"🎁 {scaledDailyXP} XP";
                if (questStreak > 0)
                {
                    TxtDailyStreakBonus.Text = $"(+{questStreak * 3}%\U0001f525)";
                    TxtDailyStreakBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtDailyStreakBonus.Visibility = Visibility.Collapsed;
                }
                if (rerollMult > 1.0)
                {
                    TxtDailyRerollBonus.Text = $"(+{(int)((rerollMult - 1.0) * 100)}%\U0001f503)";
                    TxtDailyRerollBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtDailyRerollBonus.Visibility = Visibility.Collapsed;
                }

                // Load quest image (supports remote cached images)
                try
                {
                    var dailyImagePath = GetModeAwareQuestImagePath(dailyDef);
                    var dailyImage = LoadQuestImage(dailyImagePath);
                    if (dailyImage != null)
                    {
                        ImgDailyQuest.Source = dailyImage;
                    }
                }
                catch { /* Image load failed, leave blank */ }

                // Update progress bar
                double progressPercent = dailyDef.TargetValue > 0
                    ? Math.Min(1.0, (double)dailyProgress.CurrentProgress / dailyDef.TargetValue)
                    : 0;
                DailyProgressFill.Width = DailyProgressTrack.ActualWidth > 0
                    ? DailyProgressTrack.ActualWidth * progressPercent
                    : 0;

                // Show completed overlay if done (briefly visible before next quest loads)
                if (dailyProgress.IsCompleted)
                {
                    DailyCompletedOverlay.Visibility = Visibility.Visible;
                    BtnRerollDaily.IsEnabled = false;
                    BtnRerollDaily.Content = Loc.Get("btn_completed");
                }
                else
                {
                    DailyCompletedOverlay.Visibility = Visibility.Collapsed;
                    int remainingRerolls = questService.GetRemainingDailyRerolls();
                    BtnRerollDaily.IsEnabled = remainingRerolls > 0;
                    BtnRerollDaily.Content = remainingRerolls > 0 ? $"🔄 Reroll ({remainingRerolls} left)" : "🔄 No rerolls left";
                }
            }

            // Refresh weekly quest display
            var weeklyDef = questService.GetCurrentWeeklyDefinition();
            var weeklyProgress = questService.Progress.WeeklyQuest;
            if (weeklyDef != null && weeklyProgress != null)
            {
                TxtWeeklyQuestIcon.Text = weeklyDef.Icon;
                TxtWeeklyQuestName.Text = App.Mods?.MakeModAware(weeklyDef.Name) ?? weeklyDef.Name;
                TxtWeeklyQuestDesc.Text = App.Mods?.MakeModAware(weeklyDef.Description) ?? weeklyDef.Description;
                TxtWeeklyProgress.Text = $"{weeklyProgress.CurrentProgress} / {weeklyDef.TargetValue}";
                // Show scaled XP based on level (+4% per level), reroll bonus, and streak bonus
                var wPlayerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                var wRerollMult = App.SkillTree?.GetRerollBonusMultiplier() ?? 1.0;
                var wQuestStreak = App.Settings?.Current?.DailyQuestStreak ?? 0;
                var wStreakMult = 1.0 + (wQuestStreak * 0.03);
                var scaledWeeklyXP = (int)Math.Round(weeklyDef.XPReward * (1 + wPlayerLevel * 0.04) * wRerollMult * wStreakMult);
                TxtWeeklyXP.Text = $"🎁 {scaledWeeklyXP} XP";
                if (wQuestStreak > 0)
                {
                    TxtWeeklyStreakBonus.Text = $"(+{wQuestStreak * 3}%\U0001f525)";
                    TxtWeeklyStreakBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtWeeklyStreakBonus.Visibility = Visibility.Collapsed;
                }
                if (wRerollMult > 1.0)
                {
                    TxtWeeklyRerollBonus.Text = $"(+{(int)((wRerollMult - 1.0) * 100)}%\U0001f503)";
                    TxtWeeklyRerollBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtWeeklyRerollBonus.Visibility = Visibility.Collapsed;
                }

                // Load quest image (supports remote cached images)
                try
                {
                    var weeklyImagePath = GetModeAwareQuestImagePath(weeklyDef);
                    var weeklyImage = LoadQuestImage(weeklyImagePath);
                    if (weeklyImage != null)
                    {
                        ImgWeeklyQuest.Source = weeklyImage;
                    }
                }
                catch { /* Image load failed, leave blank */ }

                // Update progress bar
                double progressPercent = weeklyDef.TargetValue > 0
                    ? Math.Min(1.0, (double)weeklyProgress.CurrentProgress / weeklyDef.TargetValue)
                    : 0;
                WeeklyProgressFill.Width = WeeklyProgressTrack.ActualWidth > 0
                    ? WeeklyProgressTrack.ActualWidth * progressPercent
                    : 0;

                // Show completed overlay if done
                if (weeklyProgress.IsCompleted)
                {
                    WeeklyCompletedOverlay.Visibility = Visibility.Visible;
                    BtnRerollWeekly.IsEnabled = false;
                    BtnRerollWeekly.Content = Loc.Get("btn_completed");
                }
                else
                {
                    WeeklyCompletedOverlay.Visibility = Visibility.Collapsed;
                    int remainingRerolls = questService.GetRemainingWeeklyRerolls();
                    BtnRerollWeekly.IsEnabled = remainingRerolls > 0;
                    BtnRerollWeekly.Content = remainingRerolls > 0 ? $"🔄 Reroll ({remainingRerolls} left)" : "🔄 No rerolls left";
                }
            }

            // Update statistics
            TxtTotalDailyCompleted.Text = questService.Progress.TotalDailyQuestsCompleted.ToString();
            TxtTotalWeeklyCompleted.Text = questService.Progress.TotalWeeklyQuestsCompleted.ToString();
            TxtTotalQuestXP.Text = questService.Progress.TotalXPFromQuests.ToString();

            // Update header stats
            int completedToday = dailyCompleted + (weeklyProgress?.IsCompleted == true ? 1 : 0);
            TxtQuestStats.Text = $"{completedToday} completed today";

            // Refresh streak calendar
            RefreshStreakCalendar();
        }

        private void RefreshStreakCalendar()
        {
            if (StreakCalendarCanvas == null) return;

            StreakCalendarCanvas.Children.Clear();

            var questService = App.Quests;
            var completedDates = new HashSet<DateTime>(
                questService?.Progress?.DailyQuestCompletionDates?.Select(d => d.Date)
                ?? Enumerable.Empty<DateTime>());

            var shieldedDates = new HashSet<DateTime>(
                App.Settings?.Current?.StreakShieldUsedDates?.Select(d => d.Date)
                ?? Enumerable.Empty<DateTime>());

            var today = DateTime.Today;

            // Show current month's days
            int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
            var days = Enumerable.Range(1, daysInMonth)
                .Select(d => new DateTime(today.Year, today.Month, d)).ToList();

            // Canvas doesn't auto-stretch, so use parent's actual width minus padding
            double canvasWidth = StreakCalendarCanvas.ActualWidth;
            if (canvasWidth <= 0)
            {
                var parent = StreakCalendarCanvas.Parent as FrameworkElement;
                canvasWidth = parent?.ActualWidth ?? 0;
            }
            if (canvasWidth <= 0) canvasWidth = 600;

            double spacing = canvasWidth / daysInMonth;
            double centerY = 25;

            double prevCenterX = 0;
            bool prevCompleted = false;
            bool hasMissedDays = false;

            string[] dayLetters = { "S", "M", "T", "W", "T", "F", "S" };

            for (int i = 0; i < days.Count; i++)
            {
                var day = days[i];
                bool isSunday = day.DayOfWeek == DayOfWeek.Sunday;
                bool isToday = day.Date == today;
                bool isCompleted = completedDates.Contains(day.Date);
                bool isFuture = day.Date > today;
                bool isMissed = !isCompleted && !isFuture && day.Date < today;

                if (isMissed) hasMissedDays = true;

                double nodeSize = isSunday ? 26 : 20;
                double centerX = spacing * i + spacing / 2.0;

                // Draw connecting line from previous node
                if (i > 0)
                {
                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = prevCenterX,
                        Y1 = centerY,
                        X2 = centerX,
                        Y2 = centerY,
                        StrokeThickness = 2,
                        Stroke = (isCompleted && prevCompleted)
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D3D60"))
                    };
                    Canvas.SetZIndex(line, 0);
                    StreakCalendarCanvas.Children.Add(line);
                }

                // Draw node (rounded rectangle to fit text)
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = nodeSize,
                    Height = nodeSize,
                    RadiusX = nodeSize / 2.0,
                    RadiusY = nodeSize / 2.0,
                    Fill = isCompleted
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"))
                        : (SolidColorBrush)Application.Current.Resources["PanelBgBrush"],
                    Stroke = isToday
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D3D60")),
                    StrokeThickness = isToday ? 2 : 1
                };

                Canvas.SetLeft(rect, centerX - nodeSize / 2.0);
                Canvas.SetTop(rect, centerY - nodeSize / 2.0);
                Canvas.SetZIndex(rect, 1);
                StreakCalendarCanvas.Children.Add(rect);

                // Day letter + day number label (e.g. "S1", "M2", "T3")
                string dayLetter = dayLetters[(int)day.DayOfWeek];
                var label = new TextBlock
                {
                    Text = $"{dayLetter}{day.Day}",
                    Foreground = isCompleted
                        ? Brushes.White
                        : isFuture
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444"))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    FontSize = 7,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, centerX - label.DesiredSize.Width / 2.0);
                Canvas.SetTop(label, centerY - label.DesiredSize.Height / 2.0);
                Canvas.SetZIndex(label, 2);
                StreakCalendarCanvas.Children.Add(label);

                // Shield overlay on days protected by streak shield
                if (shieldedDates.Contains(day.Date))
                {
                    var shieldLabel = new TextBlock
                    {
                        Text = "🛡️",
                        FontFamily = new FontFamily("Segoe UI Emoji"),
                        FontSize = 10,
                        TextAlignment = TextAlignment.Center
                    };
                    shieldLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(shieldLabel, centerX - shieldLabel.DesiredSize.Width / 2.0);
                    Canvas.SetTop(shieldLabel, centerY - nodeSize / 2.0 - shieldLabel.DesiredSize.Height + 2);
                    Canvas.SetZIndex(shieldLabel, 4);
                    StreakCalendarCanvas.Children.Add(shieldLabel);
                }

                // In fix mode, overlay a pulsing pink highlight on missed days
                if (_isStreakFixMode && isMissed)
                {
                    double highlightSize = nodeSize + 4;
                    var highlight = new System.Windows.Shapes.Rectangle
                    {
                        Width = highlightSize,
                        Height = highlightSize,
                        RadiusX = highlightSize / 2.0,
                        RadiusY = highlightSize / 2.0,
                        Fill = Brushes.Transparent,
                        Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                        StrokeThickness = 2,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = day.Date
                    };

                    // Pulsing opacity animation
                    var pulseAnim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.3,
                        Duration = TimeSpan.FromMilliseconds(600),
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                    };
                    highlight.BeginAnimation(OpacityProperty, pulseAnim);

                    highlight.MouseLeftButtonDown += StreakFixDay_Click;

                    Canvas.SetLeft(highlight, centerX - highlightSize / 2.0);
                    Canvas.SetTop(highlight, centerY - highlightSize / 2.0);
                    Canvas.SetZIndex(highlight, 3);
                    StreakCalendarCanvas.Children.Add(highlight);
                }

                prevCenterX = centerX;
                prevCompleted = isCompleted;
            }

            // Update streak text
            var streak = App.Settings?.Current?.DailyQuestStreak ?? 0;
            TxtQuestStreakCount.Text = streak > 0 ? $"\U0001f525 {streak} day streak (+{streak * 3}% XP)" : "";

            // Show/hide/enable Fix Day button based on skill, XP, season usage, and missed days
            var settings = App.Settings?.Current;
            bool hasSkill = App.SkillTree?.HasSkill("oopsie_insurance") == true;
            bool alreadyUsed = settings?.SeasonalStreakRecoveryUsed == true;
            bool hasEnoughXP = (settings?.PlayerXP ?? 0) >= 500;

            if (hasSkill)
            {
                BtnFixStreak.Visibility = Visibility.Visible;
                BtnFixStreak.IsEnabled = !_isStreakFixMode || _isStreakFixMode; // Always enabled when skill owned

                if (_isStreakFixMode)
                {
                    BtnFixStreak.Content = Loc.Get("btn_cancel_2");
                }
                else
                {
                    BtnFixStreak.Content = Loc.Get("btn_fix_day");
                }

                if (alreadyUsed)
                    BtnFixStreak.ToolTip = Loc.Get("tooltip_already_used_this_season");
                else if (!hasEnoughXP)
                    BtnFixStreak.ToolTip = Loc.Get("tooltip_requires_500_xp");
                else if (!hasMissedDays)
                    BtnFixStreak.ToolTip = Loc.Get("tooltip_no_missed_days_your_streak_is_perfect");
                else
                    BtnFixStreak.ToolTip = Loc.Get("tooltip_use_oopsie_insurance_to_fix_a_missed_day_500");
            }
            else
            {
                BtnFixStreak.Visibility = Visibility.Collapsed;
            }
        }

        private void StreakCalendarCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshStreakCalendar();
        }

        private void BtnFixStreak_Click(object sender, RoutedEventArgs e)
        {
            if (_isStreakFixMode)
            {
                ExitStreakFixMode();
                return;
            }

            // Validate prerequisites with user-friendly messages
            var settings = App.Settings?.Current;
            if (settings == null) return;
            if (App.SkillTree?.HasSkill("oopsie_insurance") != true) return;

            if (settings.SeasonalStreakRecoveryUsed)
            {
                TxtFixStreakStatus.Text = Loc.Get("label_already_used_oopsie_insurance_this_season");
                TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            // Check if there are any missed days
            var questService = App.Quests;
            var completedDates = new HashSet<DateTime>(
                questService?.Progress?.DailyQuestCompletionDates?.Select(d => d.Date)
                ?? Enumerable.Empty<DateTime>());
            var today = DateTime.Today;
            bool hasMissedDays = Enumerable.Range(1, today.Day - 1)
                .Select(d => new DateTime(today.Year, today.Month, d))
                .Any(d => !completedDates.Contains(d.Date));

            if (!hasMissedDays)
            {
                TxtFixStreakStatus.Text = Loc.Get("label_no_broken_streak_you_re_doing_great_sweetie");
                TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            if (settings.PlayerXP < 500)
            {
                TxtFixStreakStatus.Text = Loc.Get("label_not_enough_xp_you_need_500_xp_to_fix_a_day");
                TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            // Enter fix mode
            _isStreakFixMode = true;
            TxtFixStreakStatus.Text = Loc.Get("label_click_a_missed_day_to_fix_it_costs_500_xp_onc");
            TxtFixStreakStatus.Visibility = Visibility.Visible;
            RefreshStreakCalendar();
        }

        private void ExitStreakFixMode()
        {
            _isStreakFixMode = false;
            TxtFixStreakStatus.Visibility = Visibility.Collapsed;
            TxtFixStreakStatus.Text = "";
            RefreshStreakCalendar();
        }

        private async void StreakFixDay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Rectangle highlight) return;
            if (highlight.Tag is not DateTime fixDate) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Confirm with user
            var result = MessageBox.Show(
                $"Fix {fixDate:MMMM d}?\n\nThis will cost 500 XP and can only be used once per season.",
                "Oopsie Insurance",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Use server-side oopsie insurance if online
            var fixDateStr = fixDate.ToString("yyyy-MM-dd");
            if (App.ProfileSync != null && !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId))
            {
                TxtFixStreakStatus.Text = Loc.Get("label_processing");
                TxtFixStreakStatus.Visibility = Visibility.Visible;

                var (success, error, newXp) = await App.ProfileSync.UseOopsieInsuranceAsync(fixDateStr);
                if (!success)
                {
                    TxtFixStreakStatus.Text = $"❌ {error ?? "Failed to use Oopsie Insurance"}";
                    TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                    TxtFixStreakStatus.Visibility = Visibility.Visible;
                    return;
                }

                // Server succeeded - update local state
                if (newXp.HasValue)
                {
                    // Server returns total XP; convert back to current-level XP
                    var currentLevel = settings.PlayerLevel;
                    var newLevelXp = App.Progression?.GetCurrentLevelXP(currentLevel, newXp.Value) ?? (settings.PlayerXP - 500);
                    settings.PlayerXP = Math.Max(0, newLevelXp);
                }
                else
                {
                    settings.PlayerXP -= 500;
                }
                settings.SeasonalStreakRecoveryUsed = true;
            }
            else
            {
                // No cloud account
                TxtFixStreakStatus.Text = Loc.Get("label_oopsie_insurance_requires_a_cloud_account_ple");
                TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            // Add the fixed date to completion dates
            var questService = App.Quests;
            if (questService?.Progress != null)
            {
                questService.Progress.DailyQuestCompletionDates.Add(fixDate);
                questService.Save();
            }

            // Recalculate the streak
            RecalculateDailyQuestStreak();

            App.Settings?.Save();
            App.Logger?.Information("Oopsie Insurance used to fix {Date} for 500 XP (server-validated)", fixDate);

            // Exit fix mode and refresh
            _isStreakFixMode = false;
            TxtFixStreakStatus.Text = $"✅ Fixed {fixDate:MMMM d}! Streak updated.";
            TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            TxtFixStreakStatus.Visibility = Visibility.Visible;
            RefreshStreakCalendar();

            // Auto-hide status after 3 seconds
            await Task.Delay(3000);
            if (!_isStreakFixMode)
            {
                TxtFixStreakStatus.Visibility = Visibility.Collapsed;
                TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"));
            }
        }

        private void RecalculateDailyQuestStreak()
        {
            App.Quests?.RecalculateStreak();
        }

        private void BtnAchievements_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("achievements");
        }

        private void BtnCompanion_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("companion");
        }

        private void BtnLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("leaderboard");
            // Surface the Season Recap re-view button only when a persisted snapshot exists.
            try
            {
                if (BtnViewSeasonRecap != null)
                    BtnViewSeasonRecap.Visibility = Services.SeasonRecapService.HasAnySnapshot()
                        ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to update re-view button visibility");
            }
        }

        /// <summary>Re-view the most recent season's recap card from its persisted snapshot.</summary>
        private void BtnViewSeasonRecap_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("season_recap"); } catch { }
            try
            {
                var snapshot = Services.SeasonRecapService.LoadLatest();
                if (snapshot == null)
                {
                    App.Notifications?.Show(Loc.Get("recap_toast_none"), Services.NotificationType.Info);
                    return;
                }
                var vm = new ViewModels.SeasonRecapViewModel(snapshot);
                var win = new Controls.SeasonRecapWindow(vm) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to open re-view window");
            }
        }

        private void BtnLab_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("lab");
        }

        // ─── [DEBUG] Webcam smoke test — TEMPORARY, remove with the XAML card ───
        private bool _webcamDebugSubscribed;
        private int _webcamDebugBlinkCount;
        private int _webcamDebugMouthOpenCount;
        private int _webcamDebugTongueOutCount;
        private GazeSide _webcamDebugLastGaze = GazeSide.Center;
        private bool _webcamDebugLastGazeSet;
        private string _webcamDebugFaceLabel = "—";

        // Stored delegate refs so EnsureWebcamDebugSubscribed's six lambdas can
        // actually be unhooked from App.Webcam in OnClosing — the pre-existing
        // _webcamDebugSubscribed flag only blocked re-subscription, it didn't
        // tear down. Without these the lambdas (which capture `this`) hold a
        // reference to MainWindow forever.
        private Action<WebcamTrackingState>? _onDebugStateChanged;
        private Action? _onDebugFaceFound;
        private Action? _onDebugFaceLost;
        private Action? _onDebugBlink;
        private Action? _onDebugMouthOpen;
        private Action? _onDebugTongueOut;
        private Action<GazeSide>? _onDebugGazeSide;

        // Camera-active pill in the title bar — visible whenever any webcam
        // feature has the capture loop running. Stored handler so we can
        // unhook in OnClosing alongside the debug subscriptions above.
        private Action<WebcamTrackingState>? _onPillStateChanged;

        private void WireWebcamActivePill()
        {
            if (App.Webcam == null || _onPillStateChanged != null) return;

            void Update(WebcamTrackingState s)
            {
                if (WebcamActivePill != null)
                {
                    WebcamActivePill.Visibility = (s == WebcamTrackingState.Tracking || s == WebcamTrackingState.FaceLost)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
                UpdateLabTrackerUi(s);
            }

            _onPillStateChanged = Update;
            App.Webcam.OnTrackingStateChanged += _onPillStateChanged;
            // Reflect current state on wire-up — service may already be running
            // if we got here after a previous Stop/Start cycle.
            Update(App.Webcam.State);
        }

        // --- Rapid 6-blink recalibration gesture -----------------------------
        // Blinking fast 6 times in a row halts all active conditioning (keeping
        // the camera on) and offers recalibration. The blink detector enforces a
        // 500ms cooldown between fires (WebcamTrackingService.BlinkCooldownMs),
        // so 6 blinks physically span >=2.5s — a literal "6 in 2s" can't fire.
        // 3.5s is the achievable window, and still far above the natural blink
        // rate (~1 per 3-4s) so spontaneous blinking never triggers it.
        private const int RapidBlinkRecalCount = 6;
        private const int RapidBlinkRecalWindowMs = 3500;
        private readonly Queue<DateTime> _rapidBlinkTimes = new();
        private Action? _onRapidBlinkRecal;
        private bool _rapidBlinkRecalInProgress;

        private void WireRapidBlinkRecalibrateShortcut()
        {
            if (App.Webcam == null || _onRapidBlinkRecal != null) return;

            void OnBlink()
            {
                // Opt-in via the toggle shown on every webcam card.
                if (App.Settings?.Current?.BlinkRecalibrateShortcutEnabled != true) return;
                // Don't fire while a calibration window is already open (its
                // verify step asks the user to blink) or while we're mid-trigger.
                if (_rapidBlinkRecalInProgress || WebcamCalibrationWindow.IsShowing) return;

                var now = DateTime.UtcNow;
                _rapidBlinkTimes.Enqueue(now);
                var cutoff = now.AddMilliseconds(-RapidBlinkRecalWindowMs);
                while (_rapidBlinkTimes.Count > 0 && _rapidBlinkTimes.Peek() < cutoff)
                    _rapidBlinkTimes.Dequeue();

                if (_rapidBlinkTimes.Count >= RapidBlinkRecalCount)
                {
                    _rapidBlinkTimes.Clear();
                    _ = TriggerRapidBlinkRecalibrateAsync();
                }
            }

            _onRapidBlinkRecal = OnBlink;
            App.Webcam.OnBlink += _onRapidBlinkRecal;
        }

        private async Task TriggerRapidBlinkRecalibrateAsync()
        {
            if (_rapidBlinkRecalInProgress) return;
            _rapidBlinkRecalInProgress = true;
            try
            {
                App.Logger?.Information("Rapid 6-blink gesture: stopping all activity and offering recalibration.");

                // Halt everything the user is experiencing — same surface as a
                // panic press — but DELIBERATELY leave App.Webcam running: the
                // calibration window requires the capture loop to be live.
                StopAllForRecalibration();

                // Guard against a race where the capture loop stopped between the
                // triggering blink and here.
                var svc = App.Webcam;
                if (svc != null && !svc.IsRunning)
                    await Task.Run(() => svc.Start());
                if (svc == null || !svc.IsRunning)
                {
                    App.Logger?.Warning("Rapid-blink recal: webcam not running and could not be (re)started; aborting.");
                    return;
                }

                var choice = MessageBox.Show(this,
                    "You blinked to stop everything. Recalibrate webcam tracking now?",
                    "Recalibrate?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (choice != MessageBoxResult.Yes) return;

                WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Rapid-blink recalibration failed");
            }
            finally
            {
                _rapidBlinkRecalInProgress = false;
                _rapidBlinkTimes.Clear();
            }
        }

        /// <summary>
        /// Stops all active conditioning output (engine, session, videos, audio,
        /// gaze features) — mirroring the "stop" branch of the panic key, minus
        /// the window-restore / exit / achievement bookkeeping. Leaves the
        /// webcam capture loop (App.Webcam) RUNNING so recalibration can proceed.
        /// </summary>
        private void StopAllForRecalibration()
        {
            try { Controls.HelpPopover.CloseActive(); } catch { }
            try { App.KillAllAudio(); } catch (Exception ex) { App.Logger?.Warning(ex, "Recal stop: KillAllAudio failed"); }
            try { App.Autonomy?.CancelActivePulses(); } catch { }
            try { App.GazeFocus?.Stop(); } catch { }
            try { App.BlinkTrainer?.Stop(); } catch { }
            try
            {
                if (_sessionEngine != null && _sessionEngine.IsRunning && !_sessionEngine.IsPaused)
                {
                    _sessionEngine.PauseSession();
                    if (TxtPauseIcon != null) TxtPauseIcon.Text = "▶";
                    if (BtnPauseSession != null) BtnPauseSession.ToolTip = Loc.Get("tooltip_resume_session");
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Recal stop: pause session failed"); }
            try { if (_isRunning) StopEngine(); } catch (Exception ex) { App.Logger?.Warning(ex, "Recal stop: StopEngine failed"); }
            try { App.InteractionQueue?.ForceReset(); } catch { }
        }

        // --- "Blink to recalibrate" toggle, mirrored on every webcam card -----
        // A single setting (BlinkRecalibrateShortcutEnabled) surfaced as a small
        // checkbox on each webcam card. Toggling any one writes the setting and
        // keeps the others in sync.
        private bool _syncingBlinkRecalToggles;

        private void ChkBlinkRecalShortcut_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncingBlinkRecalToggles) return;
            bool val = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.BlinkRecalibrateShortcutEnabled = val;
                App.Settings?.Save();
            }
            SyncBlinkRecalToggles(val);
        }

        private void SyncBlinkRecalToggles(bool val)
        {
            _syncingBlinkRecalToggles = true;
            try
            {
                var boxes = new[]
                {
                    ChkBlinkRecalGaze, ChkBlinkRecalFocus, ChkBlinkRecalWebcamBar,
                    ChkBlinkRecalBlinkTrainer, ChkBlinkRecalDeeper
                };
                foreach (var cb in boxes)
                {
                    if (cb != null && cb.IsChecked != val) cb.IsChecked = val;
                }
            }
            finally { _syncingBlinkRecalToggles = false; }
        }

        // Lab redesign: reflect tracker state on the Eyes engine-bar status pill and
        // dim the two Eyes cards (with "start tracking" hints) when the tracker is off.
        // Additive — keyed off the same OnTrackingStateChanged path as the title pill.
        private void UpdateLabTrackerUi(WebcamTrackingState s)
        {
            bool live = (s == WebcamTrackingState.Tracking || s == WebcamTrackingState.FaceLost);
            var green = TryFindResource("SuccessGreenBrush") as Brush;
            var muted = TryFindResource("TextMutedBrush") as Brush;
            var panelAccent = TryFindResource("PanelAccentBrush") as Brush;

            if (LabTrackerDot != null) LabTrackerDot.Fill = live ? (green ?? LabTrackerDot.Fill) : (muted ?? LabTrackerDot.Fill);
            if (LabTrackerPill != null) LabTrackerPill.BorderBrush = live ? (green ?? LabTrackerPill.BorderBrush) : (panelAccent ?? LabTrackerPill.BorderBrush);

            if (LabGazeCard != null) LabGazeCard.Opacity = live ? 1.0 : 0.62;
            if (LabFocusCard != null) LabFocusCard.Opacity = live ? 1.0 : 0.62;
            if (LabGazeNeedsTracker != null) LabGazeNeedsTracker.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
            if (LabFocusNeedsTracker != null) LabFocusNeedsTracker.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        }

        private void WebcamActivePill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Click is the panic-stop affordance. Stops every consumer that
            // shares App.Webcam — Webcam Triggers, Focus Gaze, Blink Trainer,
            // Gaze Minigame all release together when the service stops.
            try { App.GazeFocus?.Stop(); } catch { }
            try { App.BlinkTrainer?.Stop(); } catch { }
            try { App.Webcam?.Stop(); } catch { }
        }

        private async void BtnWebcamDebugStart_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("App.Webcam is null — service not initialized");
                return;
            }

            if (svc.IsRunning)
            {
                svc.Stop();
                BtnWebcamDebugStart.Content = "Start tracking";
                AppendWebcamDebugLog("Stop requested.");
                RefreshBlinkTrainerTrackerButton();
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var dlg = new WebcamConsentDialog { Owner = this };
                var ok = dlg.ShowDialog();
                if (ok != true || !dlg.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined or dialog cancelled.");
                    return;
                }
                AppendWebcamDebugLog("Consent granted.");
            }

            EnsureWebcamDebugSubscribed();
            _webcamDebugBlinkCount = 0;
            _webcamDebugMouthOpenCount = 0;
            _webcamDebugTongueOutCount = 0;
            _webcamDebugLastGazeSet = false;
            _webcamDebugFaceLabel = "—";
            UpdateWebcamDebugCounters();

            var started = await StartWebcamOffUiThreadAsync(svc);
            if (started)
            {
                BtnWebcamDebugStart.Content = "Stop tracking";
                AppendWebcamDebugLog("Start() returned true — capture thread launching.");
            }
            else
            {
                AppendWebcamDebugLog($"Start() returned false. State={svc.State}. See logs/app.log.");
            }
            RefreshBlinkTrainerTrackerButton();
        }

        // Webcam Start() does VideoCapture open + 3 ONNX InferenceSession ctors
        // synchronously. On slow USB negotiation or driver-init paths that can
        // block 10-30s; doing it on the UI thread freezes the window long
        // enough for Windows' "not responding" reaper to terminate the app
        // (XTNSN's BUG-T3HE68DHXY pattern: instant freeze on click → silent
        // crash 10-15s later, no managed exception). Hop to a worker thread.
        private async Task<bool> StartWebcamOffUiThreadAsync(WebcamTrackingService svc)
        {
            AppendWebcamDebugLog("Starting webcam (camera open + model load can take a few seconds)…");
            if (TxtWebcamDebugStatus != null) TxtWebcamDebugStatus.Text = "Starting…";
            // The movable loading splash is driven globally off the service's
            // OnStartupProgress event (see InstallWebcamLoadingSplash), so it
            // shows no matter which code path calls Start() — not just this one.
            try
            {
                return await Task.Run(() => svc.Start());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MainWindow: webcam Start() threw on worker thread");
                AppendWebcamDebugLog($"Start() threw: {ex.Message}");
                return false;
            }
        }

        // The webcam loading splash is shown/updated/closed purely in response
        // to WebcamTrackingService.OnStartupProgress (fired from inside Start())
        // and OnTrackingStateChanged. Wiring it here, once, means every entry
        // point that starts the engine — the Lab debug button, calibration, the
        // Blink Trainer, the enhanced-video / browser nudges, the mandatory
        // video player — gets the splash without each call site knowing about
        // it. All these events are marshalled to the UI thread by the service.
        private WebcamLoadingSplash? _webcamLoadingSplash;
        private Action<double, string>? _onWebcamStartupProgress;
        private Action<WebcamTrackingState>? _onWebcamStartupState;

        private void InstallWebcamLoadingSplash()
        {
            if (App.Webcam == null || _onWebcamStartupProgress != null) return;

            _onWebcamStartupProgress = (progress, status) =>
            {
                try
                {
                    if (progress >= 1.0)
                    {
                        // Engine is up — show the bar full for a beat, then fade.
                        _webcamLoadingSplash?.SetProgress(1.0, status);
                        _webcamLoadingSplash?.CloseSplash();
                        return;
                    }

                    if (_webcamLoadingSplash == null)
                    {
                        // Don't pop a splash if the main window isn't on screen
                        // (e.g. minimized to tray during a background start).
                        if (!IsVisible) return;
                        var splash = new WebcamLoadingSplash { Owner = this };
                        splash.Closed += (s, e) =>
                        {
                            if (ReferenceEquals(_webcamLoadingSplash, splash)) _webcamLoadingSplash = null;
                        };
                        _webcamLoadingSplash = splash;
                        splash.Show();
                    }
                    _webcamLoadingSplash.SetProgress(progress, status);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "MainWindow: webcam loading splash update failed");
                }
            };

            _onWebcamStartupState = state =>
            {
                if (_webcamLoadingSplash == null) return;
                // Start() failed or was aborted before reaching 1.0. Surface WHY
                // rather than letting the bar silently vanish (#300) or hang
                // forever waiting on a wedged camera open (#311).
                switch (state)
                {
                    case WebcamTrackingState.CameraInUse:
                        _webcamLoadingSplash.ShowErrorAndClose(
                            "Camera unavailable — it may be in use by another app, or blocked by antivirus / Windows camera privacy.");
                        break;
                    case WebcamTrackingState.CameraDenied:
                        _webcamLoadingSplash.ShowErrorAndClose(
                            "Camera access denied — enable it in Windows Settings ▸ Privacy ▸ Camera, then try again.");
                        break;
                    case WebcamTrackingState.Error:
                        _webcamLoadingSplash.ShowErrorAndClose(
                            "Eye-tracking engine failed to start. See the webcam debug log for details.");
                        break;
                    case WebcamTrackingState.Stopped:
                        _webcamLoadingSplash.CloseSplash();
                        break;
                }
            };

            App.Webcam.OnStartupProgress += _onWebcamStartupProgress;
            App.Webcam.OnTrackingStateChanged += _onWebcamStartupState;
        }

        private void EnsureWebcamDebugSubscribed()
        {
            if (_webcamDebugSubscribed || App.Webcam == null) return;
            _webcamDebugSubscribed = true;

            _onDebugStateChanged = s =>
            {
                if (TxtWebcamDebugStatus != null) TxtWebcamDebugStatus.Text = s.ToString();
                AppendWebcamDebugLog($"State → {s}");
                if (s == WebcamTrackingState.Stopped || s == WebcamTrackingState.Error
                    || s == WebcamTrackingState.CameraInUse || s == WebcamTrackingState.CameraDenied)
                {
                    if (BtnWebcamDebugStart != null) BtnWebcamDebugStart.Content = "Start tracking";
                }
            };
            _onDebugFaceFound = () =>
            {
                _webcamDebugFaceLabel = "yes";
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog("Face FOUND");
            };
            _onDebugFaceLost = () =>
            {
                _webcamDebugFaceLabel = "lost";
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog("Face LOST");
            };
            _onDebugBlink = () =>
            {
                _webcamDebugBlinkCount++;
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog($"Blink #{_webcamDebugBlinkCount}");
            };
            _onDebugMouthOpen = () =>
            {
                _webcamDebugMouthOpenCount++;
                AppendWebcamDebugLog($"Mouth-open #{_webcamDebugMouthOpenCount}");
            };
            _onDebugTongueOut = () =>
            {
                _webcamDebugTongueOutCount++;
                AppendWebcamDebugLog($"Tongue-out #{_webcamDebugTongueOutCount}");
            };
            _onDebugGazeSide = side =>
            {
                // Only log on CHANGE — gaze side fires every frame and would
                // otherwise drown out blinks and face events.
                if (_webcamDebugLastGazeSet && side == _webcamDebugLastGaze)
                {
                    _webcamDebugLastGaze = side;
                    return;
                }
                _webcamDebugLastGaze = side;
                _webcamDebugLastGazeSet = true;
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog($"Gaze → {side}");
            };

            App.Webcam.OnTrackingStateChanged += _onDebugStateChanged;
            App.Webcam.OnFaceFound += _onDebugFaceFound;
            App.Webcam.OnFaceLost += _onDebugFaceLost;
            App.Webcam.OnBlink += _onDebugBlink;
            App.Webcam.OnMouthOpen += _onDebugMouthOpen;
            App.Webcam.OnTongueOut += _onDebugTongueOut;
            App.Webcam.OnGazeSide += _onDebugGazeSide;
        }

        private void UnsubscribeWebcamDebug()
        {
            if (!_webcamDebugSubscribed || App.Webcam == null) return;
            if (_onDebugStateChanged != null) App.Webcam.OnTrackingStateChanged -= _onDebugStateChanged;
            if (_onDebugFaceFound    != null) App.Webcam.OnFaceFound -= _onDebugFaceFound;
            if (_onDebugFaceLost     != null) App.Webcam.OnFaceLost  -= _onDebugFaceLost;
            if (_onDebugBlink        != null) App.Webcam.OnBlink     -= _onDebugBlink;
            if (_onDebugMouthOpen    != null) App.Webcam.OnMouthOpen -= _onDebugMouthOpen;
            if (_onDebugTongueOut    != null) App.Webcam.OnTongueOut -= _onDebugTongueOut;
            if (_onDebugGazeSide     != null) App.Webcam.OnGazeSide  -= _onDebugGazeSide;
            _webcamDebugSubscribed = false;
        }

        private void UpdateWebcamDebugCounters()
        {
            if (TxtWebcamDebugCounters == null) return;
            var gaze = _webcamDebugLastGazeSet ? _webcamDebugLastGaze.ToString() : "—";
            TxtWebcamDebugCounters.Text = $"Face: {_webcamDebugFaceLabel} | Blinks: {_webcamDebugBlinkCount} | Gaze: {gaze}";
        }

        private async void BtnWebcamDebugCalibrate_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("App.Webcam is null — service not initialized");
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined.");
                    return;
                }
            }

            // Calibration window expects the service to be running so OnRawIris fires.
            EnsureWebcamDebugSubscribed();
            var startedHere = false;
            if (!svc.IsRunning)
            {
                if (!await StartWebcamOffUiThreadAsync(svc))
                {
                    AppendWebcamDebugLog($"Couldn't start tracking. State={svc.State}.");
                    return;
                }
                startedHere = true;
                BtnWebcamDebugStart.Content = "Stop tracking";
            }

            AppendWebcamDebugLog("Opening calibration window…");
            var result = WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);

            if (result == true)
            {
                AppendWebcamDebugLog("Calibration applied. Gaze classification should now be much more accurate.");
            }
            else
            {
                AppendWebcamDebugLog("Calibration cancelled or failed.");
            }

            // Leave the service running if the user manually started it earlier.
            // Only auto-stop if calibration was the only reason it's running.
            if (startedHere && result != true)
            {
                svc.Stop();
                BtnWebcamDebugStart.Content = "Start tracking";
            }

            // Cross-tab propagation (Cleanup 2 + Phase D): the Blink Trainer
            // page shows calibration status AND has a NeedsCalibration status
            // state; refresh both surfaces if the user has visited the tab.
            RefreshBlinkTrainerWebcamColumn();
            RefreshBlinkTrainerStatusRow();
        }

        private void BtnGazeMinigame_Click(object sender, RoutedEventArgs e)
        {
            new Lab.GazeMinigame.GazeMinigameWindow { Owner = this }.Show();
        }

        // ─── Focus Gaze (Lab) ──────────────────────────────────────────
        private bool _focusGazeSyncing;

        private void HookFocusGazeService()
        {
            if (App.GazeFocus == null) return;

            // Belt-and-suspenders: stop GazeFocus before WPF checks its window
            // count on MainWindow close. Without this, the cursor window can
            // keep the OnLastWindowClose process alive — App.OnExit then never
            // runs, leaving Webcam.Dispose uncalled and the camera lit.
            Closing += (_, _) => App.GazeFocus?.Stop();

            App.GazeFocus.OnActiveChanged += active =>
            {
                // Service may stop itself (e.g., webcam death) — keep the
                // toggle visually in sync without re-entering the handler.
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(() => SyncFocusGazeToggle(active));
                    return;
                }
                SyncFocusGazeToggle(active);
            };
        }

        private void SyncFocusGazeToggle(bool active)
        {
            if (ChkFocusGaze == null) return;
            if (ChkFocusGaze.IsChecked == active) return;
            _focusGazeSyncing = true;
            try { ChkFocusGaze.IsChecked = active; }
            finally { _focusGazeSyncing = false; }
            if (TxtFocusGazeStatus != null && !active) TxtFocusGazeStatus.Text = "";
        }

        private async void ChkFocusGaze_Changed(object sender, RoutedEventArgs e)
        {
            if (_focusGazeSyncing) return;
            if (App.GazeFocus == null) return;

            var on = ChkFocusGaze.IsChecked == true;
            if (on)
            {
                if (!WebcamTrackingService.IsConsentCurrent())
                {
                    var dlg = new WebcamConsentDialog { Owner = this };
                    var ok = dlg.ShowDialog();
                    if (ok != true || !dlg.ConsentGiven)
                    {
                        SyncFocusGazeToggle(false);
                        if (TxtFocusGazeStatus != null) TxtFocusGazeStatus.Text = Localization.Loc.Get("label_focus_gaze_consent_required");
                        return;
                    }
                }

                // Pre-warm the webcam off the UI thread so GazeFocus.Start —
                // which would otherwise call WebcamTrackingService.Start
                // synchronously — finds it already running and just subscribes.
                if (App.Webcam != null && !App.Webcam.IsRunning)
                {
                    if (TxtFocusGazeStatus != null) TxtFocusGazeStatus.Text = "Starting webcam…";
                    var started = await Task.Run(() => App.Webcam.Start());
                    if (!started)
                    {
                        SyncFocusGazeToggle(false);
                        if (TxtFocusGazeStatus != null)
                            TxtFocusGazeStatus.Text = Localization.Loc.GetF("label_focus_gaze_webcam_failed_format", App.Webcam?.State);
                        return;
                    }
                }

                if (App.GazeFocus.Start())
                {
                    if (TxtFocusGazeStatus != null) TxtFocusGazeStatus.Text = Localization.Loc.Get("label_focus_gaze_active");
                }
                else
                {
                    SyncFocusGazeToggle(false);
                    if (TxtFocusGazeStatus != null)
                    {
                        if (App.Webcam?.Calibration == null)
                            TxtFocusGazeStatus.Text = Localization.Loc.Get("label_focus_gaze_calibrate_first");
                        else
                            TxtFocusGazeStatus.Text = Localization.Loc.GetF("label_focus_gaze_webcam_failed_format", App.Webcam?.State);
                    }
                }
            }
            else
            {
                App.GazeFocus.Stop();
                if (TxtFocusGazeStatus != null) TxtFocusGazeStatus.Text = "";
            }
        }

        // ─── Blink Trainer — service lifecycle + Running countdown ───────
        // The configurator + stage UI now lives on the dedicated Exclusives
        // page (see "Blink Trainer Tab — *" regions). What's left here is the
        // service-side glue: hook StateChanged, run the 1Hz countdown timer
        // that the new status row's Running text depends on, and stop on
        // window close.
        private DispatcherTimer? _blinkTrainerTickTimer;

        private void HookBlinkTrainerService()
        {
            if (App.BlinkTrainer == null) return;

            // Stop on MainWindow close so the camera doesn't stay lit if the
            // overlay window is the only thing keeping the process alive.
            Closing += (_, _) => App.BlinkTrainer?.Stop();

            App.BlinkTrainer.StateChanged += () =>
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(OnBlinkTrainerServiceStateChanged);
                    return;
                }
                OnBlinkTrainerServiceStateChanged();
            };

            // Defensive: if the service is somehow already running at hook
            // time (shouldn't happen — hook runs at window load before the
            // user can start anything — but cheap insurance), make sure the
            // countdown timer is wired.
            SyncBlinkTrainerCountdownTimer();
        }

        /// <summary>
        /// Single fan-out for BlinkTrainerService.StateChanged. Updates the
        /// new Exclusives tab's status row + stage mode and manages the
        /// per-second countdown timer that BlinkTrainerTick drives.
        /// </summary>
        private void OnBlinkTrainerServiceStateChanged()
        {
            try { SyncBlinkTrainerCountdownTimer(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "SyncBlinkTrainerCountdownTimer failed"); }

            try { RefreshBlinkTrainerStatusRow(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "RefreshBlinkTrainerStatusRow failed"); }

            try { ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode()); }
            catch (Exception ex) { App.Logger?.Warning(ex, "ApplyBlinkTrainerStageMode failed"); }
        }

        /// <summary>
        /// Starts / stops the 1Hz countdown timer to match the service's
        /// running state. Idempotent. Drives the new Exclusives tab's Running
        /// status text via BlinkTrainerTick.
        /// </summary>
        private void SyncBlinkTrainerCountdownTimer()
        {
            if (App.BlinkTrainer == null) return;
            var running = App.BlinkTrainer.IsRunning;

            if (running)
            {
                if (_blinkTrainerTickTimer == null)
                {
                    _blinkTrainerTickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _blinkTrainerTickTimer.Tick += BlinkTrainerTick;
                    _blinkTrainerTickTimer.Start();
                }
                BlinkTrainerTick(this, EventArgs.Empty);
            }
            else
            {
                if (_blinkTrainerTickTimer != null)
                {
                    try { _blinkTrainerTickTimer.Stop(); } catch { }
                    _blinkTrainerTickTimer.Tick -= BlinkTrainerTick;
                    _blinkTrainerTickTimer = null;
                }
            }
        }

        private void BlinkTrainerTick(object? sender, EventArgs e)
        {
            if (App.BlinkTrainer == null || !App.BlinkTrainer.IsRunning) return;
            var rem = App.BlinkTrainer.Remaining;

            // New Exclusives tab status text — only overwrite while we're
            // displaying the Running state. Other states (Error / NeedsX) get
            // their own copy from ApplyBlinkTrainerStatusState.
            if (BlinkTrainerStatusText != null && _currentBlinkTrainerStatusState == BlinkTrainerStatusState.Running)
                BlinkTrainerStatusText.Text = Localization.Loc.GetF("blink_trainer_status_running", rem.ToString(rem.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss"));
        }

        /// <summary>
        /// Lab "Moved to Exclusives" stub navigates to the new home.
        /// </summary>
        private void BtnLabBlinkTrainerOpenNew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowTab("blinktrainer");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lab Blink Trainer stub navigation failed");
            }
        }

        private async void BtnWebcamDebugTrackerTest_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("App.Webcam is null — service not initialized");
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined.");
                    return;
                }
            }

            // Tracker test needs the service running so OnGazeMove fires, AND a
            // calibration loaded so there's a homography to project through.
            EnsureWebcamDebugSubscribed();
            var startedHere = false;
            if (!svc.IsRunning)
            {
                if (!await StartWebcamOffUiThreadAsync(svc))
                {
                    AppendWebcamDebugLog($"Couldn't start tracking. State={svc.State}.");
                    return;
                }
                startedHere = true;
                BtnWebcamDebugStart.Content = "Stop tracking";
            }

            if (svc.Calibration == null)
            {
                AppendWebcamDebugLog("No calibration loaded — run Calibrate (16-point) first.");
                if (startedHere) { svc.Stop(); BtnWebcamDebugStart.Content = "Start tracking"; }
                return;
            }

            AppendWebcamDebugLog("Opening tracker test window…");
            var trackerDlg = new WebcamGazeTrackerWindow { Owner = this };
            App.ApplyCalibrationScreenPlacement(trackerDlg);
            trackerDlg.ShowDialog();
            AppendWebcamDebugLog("Tracker test closed.");

            // Match calibration handler's lifetime: only auto-stop tracking if we
            // were the ones that started it. If the user already had it running,
            // leave it running.
            if (startedHere)
            {
                svc.Stop();
                BtnWebcamDebugStart.Content = "Start tracking";
            }
        }

        private async void BtnWebcamDebugQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("Webcam service unavailable.");
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined.");
                    return;
                }
            }

            EnsureWebcamDebugSubscribed();
            var startedHere = false;
            if (!svc.IsRunning)
            {
                if (!await StartWebcamOffUiThreadAsync(svc))
                {
                    AppendWebcamDebugLog($"Couldn't start tracking. State={svc.State}.");
                    return;
                }
                startedHere = true;
                BtnWebcamDebugStart.Content = "Stop tracking";
            }

            if (svc.Calibration == null)
            {
                AppendWebcamDebugLog("No calibration loaded — run Calibrate (16-point) first. Quick Recal only nudges an existing calibration.");
                if (startedHere) { svc.Stop(); BtnWebcamDebugStart.Content = "Start tracking"; }
                return;
            }

            AppendWebcamDebugLog("Opening quick-recal window…");
            var recalDlg = new WebcamQuickRecalWindow { Owner = this };
            App.ApplyCalibrationScreenPlacement(recalDlg);
            var result = recalDlg.ShowDialog();
            AppendWebcamDebugLog(result == true
                ? $"Quick recal applied (offset {svc.Calibration.RuntimeOffset?.Dx:F0}, {svc.Calibration.RuntimeOffset?.Dy:F0} px)."
                : "Quick recal cancelled.");

            if (startedHere)
            {
                svc.Stop();
                BtnWebcamDebugStart.Content = "Start tracking";
            }

            // Cross-tab propagation (Cleanup 2 + Phase D).
            RefreshBlinkTrainerWebcamColumn();
            RefreshBlinkTrainerStatusRow();
        }

        private void BtnWebcamReviewPrivacy_Click(object sender, RoutedEventArgs e)
        {
            // Re-open the consent flow for users who want to read the privacy
            // contract again after they've already agreed. The dialog only
            // overwrites WebcamConsentGiven when the user explicitly walks
            // through the gates and clicks Enable — Cancel/close leaves the
            // existing consent state alone, so this is safe to invoke any
            // time as a "review only" path.
            try
            {
                var dlg = new WebcamConsentDialog { Owner = this };
                dlg.ShowDialog();
                AppendWebcamDebugLog("Privacy info reviewed.");
                // Cross-tab propagation (Cleanup 2 + Phase D): consent may have
                // been toggled via the dialog's Enable path.
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Webcam review privacy dialog failed");
            }
        }

        private void BtnWebcamRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    this,
                    "Revoke webcam consent?\n\n" +
                    "This will:\n" +
                    "  • Stop webcam tracking immediately\n" +
                    "  • Delete your calibration data\n" +
                    "  • Disable Focus Gaze and any webcam triggers\n" +
                    "  • Clear your consent record\n\n" +
                    "You'll be re-prompted to consent and recalibrate the next time you enable a webcam feature.",
                    "Revoke webcam consent",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);

                if (result != MessageBoxResult.OK) return;

                App.Webcam?.RevokeConsent();
                if (ChkWebcamDebugCursor != null) ChkWebcamDebugCursor.IsChecked = false;
                AppendWebcamDebugLog("Consent revoked. Calibration deleted; webcam features disabled.");

                // Cross-tab propagation (Cleanup 2 + Phase D).
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Webcam revoke consent failed");
            }
        }

        private void ChkWebcamDebugCursor_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkWebcamDebugCursor == null) return;
            if (ChkWebcamDebugCursor.IsChecked == true)
            {
                App.GazeCursor?.Show("debug-toggle");
                AppendWebcamDebugLog("Debug cursor enabled. Tracking must be running + calibrated for the dot to appear.");
            }
            else
            {
                App.GazeCursor?.Hide("debug-toggle");
                AppendWebcamDebugLog("Debug cursor hidden.");
            }
        }

        private void ChkRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _restrictGazeCheckboxSyncing) return;
            if (ChkRestrictGazeToCalScreen == null || App.Settings?.Current == null) return;
            bool v = ChkRestrictGazeToCalScreen.IsChecked == true;
            App.Settings.Current.RestrictGazeContentToCalibratedScreen = v;
            MirrorRestrictGazeToOtherCards(v, except: ChkRestrictGazeToCalScreen);
        }

        // Re-entrancy guard for cross-tab Restrict-gaze checkbox sync (Lab,
        // Blink Trainer, Deeper hub all bind the same AppSettings flag).
        private bool _restrictGazeCheckboxSyncing;

        private void ChkBlinkTrainerRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _restrictGazeCheckboxSyncing) return;
            if (ChkBlinkTrainerRestrictGazeToCalScreen == null || App.Settings?.Current == null) return;
            bool v = ChkBlinkTrainerRestrictGazeToCalScreen.IsChecked == true;
            App.Settings.Current.RestrictGazeContentToCalibratedScreen = v;
            MirrorRestrictGazeToOtherCards(v, except: ChkBlinkTrainerRestrictGazeToCalScreen);
        }

        /// <summary>
        /// Sync the Restrict-gaze checkbox across the three cards (Lab, Blink
        /// Trainer, Deeper hub) without re-entering the change-handler save
        /// path. The guard makes the mirrored .IsChecked assignment a no-op
        /// from each handler's POV.
        /// </summary>
        private void MirrorRestrictGazeToOtherCards(bool value, System.Windows.Controls.CheckBox? except)
        {
            _restrictGazeCheckboxSyncing = true;
            try
            {
                if (ChkRestrictGazeToCalScreen != null
                    && ChkRestrictGazeToCalScreen != except
                    && ChkRestrictGazeToCalScreen.IsChecked != value)
                    ChkRestrictGazeToCalScreen.IsChecked = value;

                if (ChkBlinkTrainerRestrictGazeToCalScreen != null
                    && ChkBlinkTrainerRestrictGazeToCalScreen != except
                    && ChkBlinkTrainerRestrictGazeToCalScreen.IsChecked != value)
                    ChkBlinkTrainerRestrictGazeToCalScreen.IsChecked = value;

                if (ChkDeeperWebcamRestrictGazeToCalScreen != null
                    && ChkDeeperWebcamRestrictGazeToCalScreen != except
                    && ChkDeeperWebcamRestrictGazeToCalScreen.IsChecked != value)
                    ChkDeeperWebcamRestrictGazeToCalScreen.IsChecked = value;
            }
            finally { _restrictGazeCheckboxSyncing = false; }
        }


        // Suppresses the SelectionChanged save while we programmatically
        // (re)populate either webcam ComboBox during enumeration / restore /
        // cross-tab sync. Single flag covers BOTH Lab + Blink Trainer combos
        // so a populate-one-then-the-other sequence inside
        // PopulateWebcamDeviceCombos doesn't trip the save path mid-loop.
        private bool _webcamDevicePopulating;

        /// <summary>
        /// Single enumeration → both combos. The Lab (CmbWebcamDevice) and
        /// the Blink Trainer page (CmbBlinkTrainerWebcamDevice) share device
        /// state via AppSettings.WebcamDeviceIndex but historically only
        /// re-populated on their own tab's entry — leaving the other combo
        /// stale until the user navigated to it. This helper rebuilds both
        /// at once. Safe to call when one combo's parent tab hasn't been
        /// loaded yet (null check inside PopulateWebcamCombo).
        /// </summary>
        private void PopulateWebcamDeviceCombos()
        {
            if (App.Webcam == null) return;
            var devices = App.Webcam.EnumerateDevices();
            _webcamDevicePopulating = true;
            try
            {
                PopulateWebcamCombo(CmbWebcamDevice, devices);
                PopulateWebcamCombo(CmbBlinkTrainerWebcamDevice, devices);
                PopulateWebcamCombo(CmbDeeperWebcamDevice, devices);
            }
            finally
            {
                _webcamDevicePopulating = false;
            }
        }

        private static void PopulateWebcamCombo(
            ComboBox? cb,
            IReadOnlyList<Services.WebcamDeviceEnumerator.WebcamDevice> devices)
        {
            if (cb == null) return;
            cb.Items.Clear();
            if (devices.Count == 0)
            {
                cb.Items.Add(new ComboBoxItem
                {
                    Content = "(no cameras detected)",
                    Tag = -1,
                    IsEnabled = false,
                });
                cb.SelectedIndex = 0;
                return;
            }
            foreach (var d in devices)
            {
                cb.Items.Add(new ComboBoxItem
                {
                    Content = $"[{d.Index}] {d.Name}",
                    Tag = d.Index,
                });
            }
            int saved = App.Settings?.Current?.WebcamDeviceIndex ?? -1;
            int target = saved >= 0 && saved < devices.Count ? saved : 0;
            cb.SelectedIndex = target;
        }

        /// <summary>
        /// After a user selects a device on one combo, sync the other combo's
        /// SelectedIndex to match so the two surfaces don't visually diverge.
        /// Uses the _webcamDevicePopulating guard to suppress the partner
        /// combo's SelectionChanged save path.
        /// </summary>
        private void SyncWebcamComboSelections(int idx)
        {
            _webcamDevicePopulating = true;
            try
            {
                SelectComboByDeviceIndex(CmbWebcamDevice, idx);
                SelectComboByDeviceIndex(CmbBlinkTrainerWebcamDevice, idx);
                SelectComboByDeviceIndex(CmbDeeperWebcamDevice, idx);
            }
            finally { _webcamDevicePopulating = false; }
        }

        private static void SelectComboByDeviceIndex(ComboBox? cb, int idx)
        {
            if (cb == null) return;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem cbi && cbi.Tag is int t && t == idx)
                {
                    if (cb.SelectedIndex != i) cb.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>
        /// Kept for backwards-compat with existing callers (Lab ShowTab,
        /// BtnWebcamDeviceRefresh_Click). Both combos refresh in one pass.
        /// </summary>
        private void RefreshWebcamDeviceList() => PopulateWebcamDeviceCombos();

        private void CmbWebcamDevice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_webcamDevicePopulating) return;
            if (CmbWebcamDevice?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not int idx || idx < 0) return;

            if (App.Settings?.Current is { } s)
            {
                if (s.WebcamDeviceIndex == idx) return;
                s.WebcamDeviceIndex = idx;
                s.WebcamDeviceName = item.Content?.ToString() ?? "";
                App.Settings.Save();
            }

            // Keep the Blink Trainer combo in lockstep if it's been instantiated.
            SyncWebcamComboSelections(idx);

            AppendWebcamDebugLog($"Camera set to {item.Content}. {(App.Webcam?.IsRunning == true ? "Stop and Start tracking to apply." : "Will be used on next Start.")}");
        }

        private void BtnWebcamDeviceRefresh_Click(object sender, RoutedEventArgs e)
        {
            PopulateWebcamDeviceCombos();
            // Report the count of actually-enumerated devices, NOT CmbWebcamDevice.Items.Count
            // — when zero cameras are found the combo holds a single "(no cameras detected)"
            // placeholder item, which made the message falsely say "1 found" (#291).
            int found = App.Webcam?.EnumerateDevices().Count ?? 0;
            AppendWebcamDebugLog(found == 0
                ? "Re-scanned cameras: none detected."
                : $"Re-scanned cameras: {found} found.");
        }

        // Re-entrancy guard so seeding the ComboBox doesn't trigger the save path.
        private bool _webcamMonitorPopulating;

        private void RefreshWebcamMonitorList()
        {
            // Populates both the Lab combo (CmbWebcamMonitor) and the Blink Trainer
            // mirror (CmbBlinkTrainerWebcamMonitor) from the same screen list, with
            // the same saved-selection lookup. The populating flag guards the
            // SelectionChanged handlers on both combos.
            _webcamMonitorPopulating = true;
            try
            {
                var screens = App.GetAllScreensCached();
                var saved = App.Settings?.Current?.WebcamCalibrationScreen ?? "Primary";

                FillMonitorCombo(CmbWebcamMonitor, screens, saved);
                FillMonitorCombo(CmbBlinkTrainerWebcamMonitor, screens, saved);
                FillMonitorCombo(CmbDeeperWebcamMonitor, screens, saved);
            }
            finally
            {
                _webcamMonitorPopulating = false;
            }
        }

        private static void FillMonitorCombo(ComboBox? cb, System.Collections.Generic.IList<System.Windows.Forms.Screen> screens, string saved)
        {
            if (cb == null) return;
            cb.Items.Clear();
            // Always include "Primary" — survives monitor reorder. GetWebcamCalibrationScreen
            // short-circuits to Screen.PrimaryScreen when set to this sentinel.
            cb.Items.Add(new ComboBoxItem
            {
                Content = Loc.Get("webcam_monitor_primary"),
                Tag = "Primary",
            });
            int n = 1;
            foreach (var s in screens)
            {
                var label = string.Format(
                    Loc.Get("webcam_monitor_item_fmt"),
                    n++,
                    s.DeviceName,
                    s.Bounds.Width,
                    s.Bounds.Height);
                cb.Items.Add(new ComboBoxItem { Content = label, Tag = s.DeviceName });
            }
            int target = 0;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem ci
                    && ci.Tag is string tag
                    && string.Equals(tag, saved, StringComparison.OrdinalIgnoreCase))
                {
                    target = i; break;
                }
            }
            cb.SelectedIndex = target;
        }

        private void CmbWebcamMonitor_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_webcamMonitorPopulating) return;
            if (CmbWebcamMonitor?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string deviceName) return;

            if (App.Settings?.Current is { } s)
            {
                if (string.Equals(s.WebcamCalibrationScreen, deviceName, StringComparison.OrdinalIgnoreCase)) return;
                s.WebcamCalibrationScreen = deviceName;
                App.Settings.Save();
            }

            SyncMonitorComboSelection(CmbBlinkTrainerWebcamMonitor, deviceName);
            SyncMonitorComboSelection(CmbDeeperWebcamMonitor, deviceName);
            AppendWebcamDebugLog($"Calibration monitor set to {item.Content}.");
        }

        private void CmbBlinkTrainerWebcamMonitor_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_webcamMonitorPopulating) return;
            if (CmbBlinkTrainerWebcamMonitor?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string deviceName) return;

            if (App.Settings?.Current is { } s)
            {
                if (string.Equals(s.WebcamCalibrationScreen, deviceName, StringComparison.OrdinalIgnoreCase)) return;
                s.WebcamCalibrationScreen = deviceName;
                App.Settings.Save();
            }

            SyncMonitorComboSelection(CmbWebcamMonitor, deviceName);
            SyncMonitorComboSelection(CmbDeeperWebcamMonitor, deviceName);
        }

        private void SyncMonitorComboSelection(ComboBox? cb, string deviceName)
        {
            if (cb == null) return;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem ci
                    && ci.Tag is string tag
                    && string.Equals(tag, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    if (cb.SelectedIndex == i) return;
                    _webcamMonitorPopulating = true;
                    try { cb.SelectedIndex = i; }
                    finally { _webcamMonitorPopulating = false; }
                    return;
                }
            }
        }

        private void AppendWebcamDebugLog(string line)
        {
            if (TxtWebcamDebugLog == null) return;
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            var existing = TxtWebcamDebugLog.Text;
            if (existing == "(events will appear here)") existing = "";
            var lines = (existing + (existing.Length > 0 ? "\n" : "") + $"[{stamp}] {line}")
                .Split('\n');
            if (lines.Length > 12) lines = lines[(lines.Length - 12)..];
            TxtWebcamDebugLog.Text = string.Join("\n", lines);
        }

        private void BtnPatreonExclusives_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the menu. With StaysOpen=True (see the Popup in XAML) the
            // button's Click event always fires reliably — outside-click closing
            // is handled by the window-level PreviewMouseDown handler set up in
            // the constructor. If the popup is already pinned-open, click closes
            // it. Otherwise click opens & pins it (so MouseLeave won't dismiss it).
            _exclusivesMenuCloseTimer?.Stop();
            if (ExclusivesSubmenuPopup.IsOpen && _exclusivesPinned)
            {
                _exclusivesPinned = false;
                ExclusivesSubmenuPopup.IsOpen = false;
                return;
            }
            RefreshExclusivesSubmenuLocks();
            _exclusivesPinned = true;
            ExclusivesSubmenuPopup.IsOpen = true;
        }

        // Walks up the visual tree (with a logical-tree fallback for content like
        // popups) checking whether `node` is `ancestor` or descended from it.
        private static bool IsVisualDescendant(DependencyObject? node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (node == ancestor) return true;
                var parent = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
                node = parent;
            }
            return false;
        }

        /// <summary>
        /// Opens the dashboard's "App Info &amp; Data" popup. This is the new home
        /// for account management (Patreon/Discord login, cloud backup, data
        /// export, privacy policy, support links) that used to live in the
        /// Patreon Exclusives tab.
        /// </summary>
        internal void ShowAppInfoPopup()
        {
            VelvetBtnAppInfo_Click(this, new RoutedEventArgs());
        }

        private void BtnAwareness_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("awareness");
        }



        private async void BtnQuickPatreonLogin_Click(object sender, RoutedEventArgs e)
        {
            await HandleQuickPatreonLoginAsync();
        }

        private async Task HandleQuickPatreonLoginAsync()
        {
            if (App.Patreon == null) return;

            if (App.Patreon.IsAuthenticated)
            {
                // Logout
                App.ProfileSync?.StopHeartbeat();
                App.Patreon.Logout();
                if (App.Discord?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Discord still active — just update Patreon UI
                    App.Patreon.UnifiedUserId = null;
                    UpdateQuickPatreonUI();
                    UpdatePatreonUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Start OAuth flow (legacy - now use LoginDialog instead)
                try
                {
                    await App.Patreon.StartOAuthFlowAsync();

                    // Use V2 unified account flow (v5.5+ with seasons system)
                    var result = await AccountService.HandlePostAuthV2Async(this, "patreon");

                    if (result.Success)
                    {
                        UpdateQuickPatreonUI();
                        UpdatePatreonUI();
                        UpdateBannerWelcomeMessage();
                        UpdateAccountLinkingUI();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Patreon login failed");
                    MessageBox.Show(
                        $"Failed to connect to Patreon.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    UpdateQuickPatreonUI();
                }
            }
        }

        private void UpdateQuickPatreonUI()
        {
            // Now managed by unified login panel
            UpdateQuickLoginUI();
        }

        private async void BtnQuickDiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            await HandleDiscordLoginAsync();
        }

        private async Task HandleDiscordLoginAsync()
        {
            if (App.Discord == null) return;

            if (App.Discord.IsAuthenticated)
            {
                // Logout
                App.Discord.Logout();
                if (App.Patreon?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Patreon still active — just update Discord UI
                    App.Discord.UnifiedUserId = null;
                    UpdateQuickDiscordUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Start OAuth flow
                SetDiscordButtonsEnabled(false);
                SetDiscordButtonsContent("Connecting...");

                try
                {
                    await App.Discord.StartOAuthFlowAsync();

                    // Use V2 unified account flow (v5.5+ with seasons system)
                    var result = await AccountService.HandlePostAuthV2Async(this, "discord");

                    if (result.Success)
                    {
                        UpdateQuickDiscordUI();
                        UpdateBannerWelcomeMessage();
                        UpdateAccountLinkingUI();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Discord login failed");
                    MessageBox.Show(
                        $"Failed to connect to Discord.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    SetDiscordButtonsEnabled(true);
                    UpdateQuickDiscordUI();
                }
            }
        }

        private void SetDiscordButtonsEnabled(bool enabled)
        {
            // Old quick button removed - now using unified login
        }

        private void SetDiscordButtonsContent(string text)
        {
            // Old quick button removed - now using unified login
        }

        private void UpdateQuickDiscordUI()
        {
            // Now managed by unified login panel
            UpdateQuickLoginUI();

            // Also update the Patreon tab Discord UI
            UpdateDiscordUI();
        }

        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discord.gg/YxVAMt4qaZ",
                    UseShellExecute = true
                });
                App.Logger?.Information("Opened Discord invite link");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Discord link");
            }
        }


        private void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // Get the state from whichever checkbox was clicked
            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;

            // Block enabling Rich Presence if Discord is not linked — prevents accidental
            // exposure for users who chose anonymous invite-code accounts
            if (isEnabled && App.Settings?.Current?.HasLinkedDiscord != true)
            {
                _isLoading = true;
                ChkDiscordRichPresence.IsChecked = false;
                ChkQuickDiscordRichPresence.IsChecked = false;
                if (ChkDiscordTabRichPresence != null) ChkDiscordTabRichPresence.IsChecked = false;
                _isLoading = false;
                MessageBox.Show(Loc.Get("msg_discord_rich_presence_requires_a_linked_disco"),
                    "Discord Not Linked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Sync all checkboxes without re-entrancy
            _isLoading = true;
            ChkDiscordRichPresence.IsChecked = isEnabled;
            ChkQuickDiscordRichPresence.IsChecked = isEnabled;
            if (ChkDiscordTabRichPresence != null) ChkDiscordTabRichPresence.IsChecked = isEnabled;
            _isLoading = false;

            App.Settings.Current.DiscordRichPresenceEnabled = isEnabled;

            if (App.DiscordRpc != null)
            {
                App.DiscordRpc.IsEnabled = isEnabled;
                App.Logger?.Information("Discord Rich Presence {Status}", isEnabled ? "enabled" : "disabled");
            }
        }


        private void InitializeLanguageSelector()
        {
            if (CmbLanguagePill == null) return;

            CmbLanguagePill.Items.Clear();
            int selectedIndex = 0;
            var currentLang = App.Settings?.Current?.Language ?? "en";

            for (int i = 0; i < LocalizationManager.AvailableLanguages.Length; i++)
            {
                var (code, displayName, shortName) = LocalizationManager.AvailableLanguages[i];
                CmbLanguagePill.Items.Add(new ComboBoxItem
                {
                    Content = $"🌐 {shortName}",
                    Tag = code,
                    ToolTip = displayName
                });
                if (code == currentLang)
                    selectedIndex = i;
            }

            CmbLanguagePill.SelectedIndex = selectedIndex;
        }

        private void CmbLanguagePill_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguagePill?.SelectedItem is not ComboBoxItem selected) return;
            var langCode = selected.Tag as string ?? "en";

            if (App.Settings?.Current != null && App.Settings.Current.Language != langCode)
            {
                App.Settings.Current.Language = langCode;
                LocalizationManager.Instance.SetLanguage(langCode);
                App.Settings.Save();

                // XAML bindings update live; code-behind strings need a restart
                if (TxtBannerSecondary != null)
                {
                    TxtBannerSecondary.Text = Loc.Get("msg_restart_to_apply");
                    TxtBannerSecondary.Opacity = 1;
                    TxtBannerSecondary.IsHitTestVisible = true;
                }
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            BtnCheckUpdates.Content = Loc.Get("btn_checking");

            try
            {
                await App.CheckForUpdatesManuallyAsync(this);
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
                BtnCheckUpdates.Content = Loc.Get("btn_check_updates");
            }
        }

        private async void BtnUpdateAvailable_Click(object sender, RoutedEventArgs e)
        {
            // If server provided a URL, open it in browser instead of auto-updating
            if (!string.IsNullOrEmpty(_serverUpdateUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _serverUpdateUrl,
                        UseShellExecute = true
                    });
                    return;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Failed to open update URL: {Error}", ex.Message);
                }
            }

            // Trigger the update installation
            await App.CheckForUpdatesManuallyAsync(this);
        }

        /// <summary>
        /// Sets the update button state in the tab bar.
        /// Called from App when an update is detected or after checking.
        /// </summary>
        public void ShowUpdateAvailableButton(bool updateAvailable)
        {
            Dispatcher.Invoke(() =>
            {
                BtnUpdateAvailable.Tag = updateAvailable ? "UpdateAvailable" : "NoUpdate";
                BtnUpdateAvailable.Content = updateAvailable ? "UPDATE" : "LATEST VERSION :3";
                BtnUpdateAvailable.ToolTip = updateAvailable
                    ? "Update Available - Click to install!"
                    : "You're on the latest version";
            });
        }


        private void AnimateTabIn(UIElement tab)
        {
            try
            {
                tab.Opacity = 0;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                tab.BeginAnimation(OpacityProperty, anim);
            }
            catch
            {
                tab.Opacity = 1;
            }
        }

        internal void ShowTab(string tab)
        {
            // Legacy redirect: the "patreon" tab was eliminated and its
            // account/data content lives in the dashboard's App Info popup now.
            // Route any legacy callers there WITHOUT disturbing the currently
            // active tab (opening a popup is overlay-style, not a tab switch).
            if (tab == "patreon")
            {
                ShowAppInfoPopup();
                return;
            }

            // Bark hook: announce navigation (gated/chanced in the rules so it isn't spammy).
            try { App.Bark?.NotifyTabNavigated(tab); } catch { }

            // Stop animations on tabs we're leaving to reduce idle CPU
            StopSeasonTitleShimmer();
            StopLockdownPulse();
            StopSkillTreeAnimations();

            // Hide all tabs
            SettingsTab.Visibility = Visibility.Collapsed;
            PresetsTab.Visibility = Visibility.Collapsed;
            ProgressionTab.Visibility = Visibility.Collapsed;
            QuestsTab.Visibility = Visibility.Collapsed;
            AchievementsTab.Visibility = Visibility.Collapsed;
            CompanionTab.Visibility = Visibility.Collapsed;
            PatreonTab.Visibility = Visibility.Collapsed;
            LeaderboardTab.Visibility = Visibility.Collapsed;
            AssetsTab.Visibility = Visibility.Collapsed;
            DiscordTab.Visibility = Visibility.Collapsed;
            EnhancementsTab.Visibility = Visibility.Collapsed;
            if (DeeperTab != null) DeeperTab.Visibility = Visibility.Collapsed;
            LabTab.Visibility = Visibility.Collapsed;
            AwarenessTab.Visibility = Visibility.Collapsed;
            if (RemoteControlTab != null) RemoteControlTab.Visibility = Visibility.Collapsed;
            if (AvailableSubjectsTab != null) AvailableSubjectsTab.Visibility = Visibility.Collapsed;
            if (BambiTakeoverTab != null) BambiTakeoverTab.Visibility = Visibility.Collapsed;
            // SP5L3: stop polling whenever we leave the Available Subjects
            // tab. Idempotent — safe to call even if not currently polling.
            App.AvailableSubjects?.StopPolling();
            if (HapticsTab != null) HapticsTab.Visibility = Visibility.Collapsed;
            if (LockdownTab != null) LockdownTab.Visibility = Visibility.Collapsed;
            if (BlinkTrainerTab != null)
            {
                // Stop the demo timer AND drop the live-mode OnBlink subscription
                // when leaving the tab so neither runs while the user is
                // elsewhere. Both are idempotent.
                if (BlinkTrainerTab.Visibility == Visibility.Visible)
                {
                    StopBlinkTrainerDemoLoop();
                    UnsubscribeBlinkTrainerLiveBlink();
                    // Reset cached mode so the next entry re-runs the resolver
                    // and starts whatever's appropriate from scratch.
                    _currentBlinkTrainerStageMode = BlinkTrainerStageMode.Demo;
                }
                BlinkTrainerTab.Visibility = Visibility.Collapsed;
            }

            // Reset all button styles to inactive. activeStyle is the primary-nav-only v6 variant —
            // quest sub-tabs and roadmap tracks use TabButtonActive directly (see lines further down).
            var inactiveStyle = FindResource("TabButton") as Style;
            var activeStyle = FindResource("TabButtonActivePrimary") as Style;
            BtnSettings.Style = inactiveStyle;
            BtnPresets.Style = inactiveStyle;
            BtnQuests.Style = inactiveStyle;
            BtnEnhancements.Style = inactiveStyle;
            if (BtnDeeper != null) BtnDeeper.Style = FindResource("TabButtonDeeper") as Style;
            if (BtnAvailableSubjects != null) BtnAvailableSubjects.Style = FindResource("TabButtonNeon") as Style;
            BtnAchievements.Style = inactiveStyle;
            BtnCompanion.Style = inactiveStyle;
            BtnLeaderboard.Style = inactiveStyle;
            BtnLab.Style = inactiveStyle;
            BtnOpenAssetsTop.Style = inactiveStyle;
            // BtnAwareness was removed from the primary tab bar — its only entry point
            // is now the Exclusives popup submenu
            // BtnPatreonExclusives keeps its inline Patreon red style defined in XAML

            switch (tab)
            {
                case "settings":
                    SettingsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SettingsTab);
                    BtnSettings.Style = activeStyle;
                    break;

                case "presets":
                    PresetsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(PresetsTab);
                    BtnPresets.Style = activeStyle;
                    break;

                // "progression" tab removed in velvet-mosaic phase 6 — its content
                // is now on the Dashboard. Legacy callers (e.g. older tutorial steps)
                // that request ShowTab("progression") fall through to the Dashboard.
                case "progression":
                    SettingsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SettingsTab);
                    BtnSettings.Style = activeStyle;
                    break;

                case "quests":
                    QuestsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(QuestsTab);
                    BtnQuests.Style = activeStyle;
                    StartSeasonTitleShimmer();
                    RefreshQuestUI();
                    break;

                case "enhancements":
                    EnhancementsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(EnhancementsTab);
                    BtnEnhancements.Style = activeStyle;
                    RefreshEnhancementsUI();
                    break;

                case "deeper":
                    if (DeeperTab != null)
                    {
                        DeeperTab.Visibility = Visibility.Visible;
                        AnimateTabIn(DeeperTab);
                        RefreshDeeperLibraryUI();
                        // Populate the Deeper-hub webcam card (device + monitor
                        // combos populate empty until something asks). Refresh
                        // also fills the consent + calibration status cells.
                        try { PopulateWebcamDeviceCombos(); } catch { }
                        try { RefreshWebcamMonitorList(); } catch { }
                        RefreshDeeperWebcamColumn();
                        RefreshBlinkTrainerTrackerButton();
                        // Refresh submission statuses on tab open (throttled) so
                        // an acceptance reflects without restarting the app.
                        _ = CheckDeeperSubmissionStatusesAsync();
                    }
                    if (BtnDeeper != null) BtnDeeper.Style = FindResource("TabButtonDeeperActive") as Style;
                    break;

                case "achievements":
                    AchievementsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AchievementsTab);
                    BtnAchievements.Style = activeStyle;
                    RefreshAllAchievementTiles();
                    UpdateAchievementCount();
                    break;

                case "companion":
                    CompanionTab.Visibility = Visibility.Visible;
                    AnimateTabIn(CompanionTab);
                    BtnCompanion.Style = activeStyle;
                    SyncCompanionTabUI();
                    InitializePhrasePresets();
                    break;

                case "lab":
                    LabTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LabTab);
                    BtnLab.Style = activeStyle;
                    RefreshWebcamDeviceList();
                    RefreshWebcamMonitorList();
                    if (ChkRestrictGazeToCalScreen != null && App.Settings?.Current != null)
                        ChkRestrictGazeToCalScreen.IsChecked = App.Settings.Current.RestrictGazeContentToCalibratedScreen;
                    break;

                // Note: "patreon" case is handled at the top of ShowTab as a
                // legacy redirect to the App Info & Data popup (Exclusives tab
                // was eliminated; account/data UI now lives in the dashboard).

                case "leaderboard":
                    LeaderboardTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LeaderboardTab);
                    BtnLeaderboard.Style = activeStyle;
                    _ = RefreshLeaderboardAsync(); // Load on first view
                    break;

                case "assets":
                    AssetsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AssetsTab);
                    BtnOpenAssetsTop.Style = activeStyle;
                    RefreshAssetTree();
                    InitializeAssetPresets();
                    if (PacksSectionEnabled) _ = RefreshPacksAsync();
                    break;

                case "discord":
                    DiscordTab.Visibility = Visibility.Visible;
                    AnimateTabIn(DiscordTab);
                    // BtnDiscordTab keeps its inline Discord blue style defined in XAML
                    UpdateDiscordTabUI();
                    break;

                case "awareness":
                    AwarenessTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AwarenessTab);
                    SyncAwarenessTabUI();
                    break;

                case "remotecontrol":
                    RemoteControlTab.Visibility = Visibility.Visible;
                    AnimateTabIn(RemoteControlTab);
                    UpdateRemoteControlUI();
                    break;

                case "availablesubjects":
                    if (AvailableSubjectsTab != null)
                    {
                        AvailableSubjectsTab.Visibility = Visibility.Visible;
                        AnimateTabIn(AvailableSubjectsTab);
                    }
                    if (BtnAvailableSubjects != null)
                        BtnAvailableSubjects.Style = FindResource("TabButtonNeonActive") as Style;
                    EnsureAvailableSubjectsBound();
                    App.AvailableSubjects?.StartPolling();
                    break;

                case "bambitakeover":
                    BambiTakeoverTab.Visibility = Visibility.Visible;
                    AnimateTabIn(BambiTakeoverTab);
                    UpdatePatreonUI();
                    break;

                case "haptics":
                    HapticsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(HapticsTab);
                    UpdatePatreonUI();
                    break;

                case "lockdown":
                    LockdownTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LockdownTab);
                    StartLockdownPulse();
                    RefreshPremiumGate(LockdownGate);
                    break;

                case "blinktrainer":
                    BlinkTrainerTab.Visibility = Visibility.Visible;
                    AnimateTabIn(BlinkTrainerTab);
                    RefreshBlinkTrainerTab();
                    break;

            }
        }

        /// <summary>
        /// Per-tab refresh hook for the Blink Trainer page. Called on every
        /// transition into the tab. Phase C: syncs all control state from
        /// settings + webcam status. Phase D will add live-mode detection
        /// (consent + folders + active session) and skip the demo when live
        /// mode takes over.
        /// </summary>
        private void RefreshBlinkTrainerTab()
        {
            // First-visit flag flip (Phase G) — suppresses the v5.9.8 flagship
            // sticky toast on next launch. Also dismisses the toast in this
            // session if it's currently showing (H.3): once the user finds
            // the feature, the announcement has done its job.
            // Isolated try/catch so a settings failure here can't keep the
            // rest of the refresh from running.
            try
            {
                if (App.Settings?.Current is { HasSeenBlinkTrainerFlagship: false } first)
                {
                    first.HasSeenBlinkTrainerFlagship = true;
                    App.Settings?.Save();

                    // Fade out the toast if it's still on screen, and persist
                    // the dismissal so it can't refire even if HasSeen somehow
                    // doesn't stick.
                    const string flagshipKey = "blink-trainer-flagship-v5.9.8";
                    App.Notifications?.Dismiss(flagshipKey);
                    if (!first.DismissedNotificationKeys.Contains(flagshipKey))
                    {
                        first.DismissedNotificationKeys.Add(flagshipKey);
                        App.Settings?.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "HasSeenBlinkTrainerFlagship flag: failed to set");
            }

            try
            {
                var s = App.Settings?.Current;
                if (s != null)
                {
                    // IncludeVideos toggle — set before rebuilding cards so count
                    // summaries use the current mode.
                    if (ToggleBlinkTrainerIncludeVideos != null)
                        ToggleBlinkTrainerIncludeVideos.IsChecked = s.BlinkTrainerIncludeVideos;

                    // Duration
                    if (SliderBlinkTrainerDurationNew != null)
                        SliderBlinkTrainerDurationNew.Value = s.BlinkTrainerDurationMinutes;
                    if (TxtBlinkTrainerDurationValue != null)
                        TxtBlinkTrainerDurationValue.Text = $"{s.BlinkTrainerDurationMinutes} min";

                    // Opacity
                    if (SliderBlinkTrainerOpacityNew != null)
                        SliderBlinkTrainerOpacityNew.Value = s.BlinkTrainerOpacity;
                    if (TxtBlinkTrainerOpacityValue != null)
                        TxtBlinkTrainerOpacityValue.Text = $"{s.BlinkTrainerOpacity}%";

                    // Mix-mode selection visual
                    SetMixModeSelection(s.BlinkTrainerMixImages);
                }

                RebuildBlinkTrainerFolderCards();
                RefreshBlinkTrainerWebcamColumn();
                // Monitor picker + Restrict-gaze checkbox mirror the Lab card.
                // RefreshWebcamMonitorList now populates both combos; the checkbox
                // gets its initial state here so the BT tab matches without
                // requiring a Lab visit first.
                RefreshWebcamMonitorList();
                if (ChkBlinkTrainerRestrictGazeToCalScreen != null && s != null)
                {
                    _restrictGazeCheckboxSyncing = true;
                    try { ChkBlinkTrainerRestrictGazeToCalScreen.IsChecked = s.RestrictGazeContentToCalibratedScreen; }
                    finally { _restrictGazeCheckboxSyncing = false; }
                }
                RefreshBlinkTrainerGate();
                RefreshBlinkTrainerTrackerButton();

                // Phase D: status row + stage mode are now state-machine driven.
                // RefreshBlinkTrainerStatusRow paints the dot/text/action button;
                // ApplyBlinkTrainerStageMode handles demo-vs-live transitions.
                // ApplyBlinkTrainerStageMode also calls StartBlinkTrainerDemoLoop
                // when it decides demo mode is appropriate.
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());

                // ApplyBlinkTrainerStageMode is a no-op when the mode hasn't
                // changed (e.g. second tab visit while already in Demo). Cover
                // the initial-show case where there's nothing to transition
                // FROM by ensuring the demo loop is running if we're in Demo.
                if (_currentBlinkTrainerStageMode == BlinkTrainerStageMode.Demo)
                    StartBlinkTrainerDemoLoop();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshBlinkTrainerTab failed");
            }
        }






        private void UpdateAchievementCount()
        {
            if (App.Achievements == null) return;

            // Free and patron counts are kept strictly separate — never summed.
            if (TxtAchievementCount != null)
            {
                var unlocked = App.Achievements.GetUnlockedCount(exclusive: false);
                var total = App.Achievements.GetTotalCount(exclusive: false);
                TxtAchievementCount.Text = Loc.GetF("label_0_1_achievements_unlocked", unlocked, total);
            }

            if (TxtPatronAchievementCount != null)
            {
                var pUnlocked = App.Achievements.GetUnlockedCount(exclusive: true);
                var pTotal = App.Achievements.GetTotalCount(exclusive: true);
                TxtPatronAchievementCount.Text = Loc.GetF("label_0_1_achievements_unlocked", pUnlocked, pTotal);
            }

            // Free users see the patron collection as a labeled, locked section.
            if (PatronAchievementsOverlay != null)
            {
                PatronAchievementsOverlay.Visibility = App.Patreon?.HasPremiumAccess == true
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        /// <summary>
        /// Sync Companion tab UI controls with current state
        /// </summary>
        private void SyncCompanionTabUI()
        {
            _isLoading = true;
            try
            {
                // Sync avatar enabled
                ChkAvatarEnabledCompanion.IsChecked = _avatarTubeWindow?.IsVisible == true;

                // Sync trigger mode
                ChkTriggerModeCompanion.IsChecked = App.Settings?.Current?.TriggerModeEnabled == true;
                TriggerSettingsPanelCompanion.Visibility = ChkTriggerModeCompanion.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

                // Sync trigger interval
                var interval = App.Settings?.Current?.TriggerIntervalSeconds ?? 60;
                SliderTriggerIntervalCompanion.Value = interval;
                TxtTriggerIntervalCompanion.Text = $"{interval}s";

                // Sync idle interval
                var idleInterval = App.Settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
                SliderIdleIntervalCompanion.Value = idleInterval;
                TxtIdleIntervalCompanion.Text = $"{idleInterval}s";

                // Sync bubble persistence duration
                var bubbleDuration = App.Settings?.Current?.BubbleDurationSeconds ?? 2.0;
                SliderBubbleDurationCompanion.Value = bubbleDuration;
                TxtBubbleDurationCompanion.Text = $"{(int)bubbleDuration}s";

                // Sync detach status
                var isDetached = _avatarTubeWindow?.IsDetached == true;
                TxtDetachStatusCompanion.Text = isDetached ? "Floating freely" : "Anchored to window";
                BtnDetachCompanionTab.Content = isDetached ? "Attach" : "Detach";

                // Sync companion leveling UI (v5.3)
                UpdateCompanionCardsUI();

                // Sync AI Brain panel + hero pills (v5.9)
                SyncAiBrainUI();
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Updates the companion selection cards UI with current progress and active state.
        /// </summary>
        private void UpdateCompanionCardsUI()
        {
            if (App.Companion == null || App.Settings?.Current == null) return;

            var activeId = App.Companion.ActiveCompanion;
            var playerLevel = App.Settings.Current.PlayerLevel;

            // Update each companion card
            var cards = new[] { CompanionCard0, CompanionCard1, CompanionCard2, CompanionCard3, CompanionCard4 };
            var levelTexts = new[] { TxtCompanion0Level, TxtCompanion1Level, TxtCompanion2Level, TxtCompanion3Level, TxtCompanion4Level };
            var lockTexts = new[] { TxtCompanion0Lock, TxtCompanion1Lock, TxtCompanion2Lock, TxtCompanion3Lock, TxtCompanion4Lock };
            var nameTexts = new[] { TxtCompanion0Name, TxtCompanion1Name, TxtCompanion2Name, TxtCompanion3Name, TxtCompanion4Name };
            var colors = new[] { App.Mods?.GetAccentColorHex() ?? "#FF69B4", "#9370DB", "#50C878", "#FF6B6B", "#F5DEB3" };

            for (int i = 0; i < 5; i++)
            {
                var companionId = (Models.CompanionId)i;
                var def = Models.CompanionDefinition.GetById(companionId);
                var progress = App.Companion.GetProgress(companionId);

                // Hide companion card if the active mod doesn't support this avatar set
                if (App.Mods?.IsCompanionSupported(companionId) == false)
                {
                    cards[i].Visibility = Visibility.Collapsed;
                    continue;
                }
                cards[i].Visibility = Visibility.Visible;

                // Update companion name with mod text replacements
                bool isSlutMode = App.Settings?.Current?.SlutModeEnabled ?? false;
                var companionName = def.GetDisplayName(isSlutMode);
                nameTexts[i].Text = App.Mods?.MakeModAware(companionName) ?? companionName;

                // All companions are unlocked from level 1
                levelTexts[i].Text = progress.IsMaxLevel ? "MAX" : $"Lv.{progress.Level}";

                // Highlight active companion with colored border
                var isActive = companionId == activeId;
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i]);
                cards[i].BorderBrush = isActive
                    ? new System.Windows.Media.SolidColorBrush(color)
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Transparent);

                // Companion lock visuals removed — every companion is available from level 1.
                lockTexts[i].Visibility = Visibility.Collapsed;
                cards[i].Opacity = 1.0;
            }

            // Update active companion details
            var activeDef = Models.CompanionDefinition.GetById(activeId);
            var activeProgress = App.Companion.ActiveProgress;

            var activeDisplayName = activeDef.GetDisplayName(App.Settings?.Current?.SlutModeEnabled ?? false);
            TxtActiveCompanionName.Text = App.Mods?.MakeModAware(activeDisplayName) ?? activeDisplayName;
            TxtActiveCompanionLevel.Text = activeProgress.IsMaxLevel ? " · MAX LEVEL" : $" · Level {activeProgress.Level}";
            TxtActiveCompanionDesc.Text = activeDef.Description;
            TxtActiveCompanionXP.Text = activeProgress.IsMaxLevel
                ? "Complete!"
                : $"{activeProgress.CurrentXP:F0} / {activeProgress.XPForNextLevel:F0} XP";

            // Update main progress bar
            PrgCompanion0.Value = activeProgress.LevelProgress * 100;
            PrgCompanion0.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[(int)activeId]));

            // Update community prompts UI
            UpdateCommunityPromptsUI();

            // Update companion prompt labels
            UpdateCompanionPromptLabels();

            // Refresh hero avatar GIF (v5.9)
            RefreshHeroAvatar();
        }

        /// <summary>
        /// Loads the active companion's pose-1 portrait into the hero avatar circle.
        /// Uses Stretch="Uniform" so the full figure shows centered inside the gradient ring,
        /// scaled down to fit, instead of being cropped (which broke for avatars whose figure
        /// isn't anchored to the top of the source PNG). Uses the same naming pattern as
        /// AvatarTubeWindow (avatar_pose1.png / avatarN_pose1.png).
        /// </summary>
        private void RefreshHeroAvatar()
        {
            if (HeroAvatarImage == null) return;
            try
            {
                var setNumber = App.Settings?.Current?.SelectedAvatarSet ?? 1;
                if (setNumber < 1)
                {
                    var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                    setNumber = AvatarTubeWindow.GetAvatarSetForLevel(playerLevel);
                }
                var prefix = setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose";
                var resourceName = $"{prefix}1.png";

                var resolved = Services.ModResourceResolver.ResolveImage(resourceName);
                if (resolved != null)
                {
                    HeroAvatarImage.Source = resolved;
                    return;
                }

                var uri = new Uri($"pack://application:,,,/Resources/{resourceName}", UriKind.Absolute);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                HeroAvatarImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load hero avatar pose");
            }
        }

        /// <summary>
        /// Gets the display name for the currently active prompt.
        /// </summary>
        private string GetActivePromptDisplayName()
        {
            var activePromptId = App.Settings?.Current?.ActiveCommunityPromptId;

            if (!string.IsNullOrEmpty(activePromptId))
            {
                var prompt = App.CommunityPrompts?.GetInstalledPrompt(activePromptId);
                return prompt?.Name ?? "Unknown";
            }
            else if (App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
            {
                return "Custom";
            }
            return "Default";
        }

        /// <summary>
        /// Updates the community prompts section UI.
        /// </summary>
        private void UpdateCommunityPromptsUI()
        {
            var activePromptId = App.Settings?.Current?.ActiveCommunityPromptId;
            var installedIds = App.Settings?.Current?.InstalledCommunityPromptIds ?? new List<string>();

            // Update the Customize button prompt name
            TxtCustomizePromptName.Text = GetActivePromptDisplayName();

            // Update active prompt display
            if (string.IsNullOrEmpty(activePromptId))
            {
                if (App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
                {
                    TxtActivePromptName.Text = Loc.Get("label_custom_edited");
                }
                else
                {
                    TxtActivePromptName.Text = Loc.Get("label_default_built_in");
                }
                BtnDeactivatePrompt.Visibility = Visibility.Collapsed;
            }
            else
            {
                var prompt = App.CommunityPrompts?.GetInstalledPrompt(activePromptId);
                TxtActivePromptName.Text = prompt != null ? $"{prompt.Name} by {prompt.Author}" : "Custom";
                BtnDeactivatePrompt.Visibility = Visibility.Visible;
            }

            // Update installed prompts list
            InstalledPromptsPanel.Children.Clear();
            if (installedIds.Count == 0)
            {
                InstalledPromptsPanel.Children.Add(new TextBlock
                {
                    Text = "No prompts installed",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
                    FontSize = 10,
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            else
            {
                foreach (var id in installedIds)
                {
                    var prompt = App.CommunityPrompts?.GetInstalledPrompt(id);
                    if (prompt == null) continue;

                    var isActive = id == activePromptId;
                    var row = CreatePromptRow(prompt, isActive);
                    InstalledPromptsPanel.Children.Add(row);
                }
            }
        }

        private FrameworkElement CreatePromptRow(Models.CommunityPrompt prompt, bool isActive)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Name + Author
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (isActive)
            {
                namePanel.Children.Add(new TextBlock
                {
                    Text = "● ",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 112, 219)),
                    FontSize = 10
                });
            }
            namePanel.Children.Add(new TextBlock
            {
                Text = prompt.Name,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 10,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
            });
            namePanel.Children.Add(new TextBlock
            {
                Text = $" by {prompt.Author}",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 96, 96)),
                FontSize = 9
            });
            Grid.SetColumn(namePanel, 0);
            grid.Children.Add(namePanel);

            // Action buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            if (!isActive)
            {
                var activateBtn = new Button
                {
                    Content = "Use",
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 112, 219)),
                    BorderThickness = new Thickness(0),
                    FontSize = 9,
                    Padding = new Thickness(6, 2, 6, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = prompt.Id
                };
                activateBtn.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag is string promptId)
                    {
                        // CCBill AI Addendum: gate community prompts that ship a SlutModePersonality
                        // when SlutMode is on. Synthesize a probe preset for the gate check.
                        var probePrompt = App.CommunityPrompts?.GetInstalledPrompt(promptId);
                        var slutModeOn = App.Settings?.Current?.SlutModeEnabled == true;
                        var probe = new Models.PersonalityPreset { PromptSettings = probePrompt?.PromptSettings };
                        if (Services.ExplicitContentGate.RequiresAcknowledgement(probe, slutModeOn))
                        {
                            var prevSettings = App.Settings?.Current?.CompanionPrompt;
                            if (!Services.ExplicitContentGate.IsAlreadyAcknowledged(prevSettings))
                            {
                                var dlg = new ExplicitContentAcknowledgementDialog { Owner = this };
                                if (dlg.ShowDialog() != true) return;
                                if (prevSettings != null)
                                {
                                    Services.ExplicitContentGate.MarkAcknowledged(prevSettings);
                                    App.Settings?.Save();
                                }
                            }
                        }

                        App.CommunityPrompts?.ActivatePrompt(promptId);
                        UpdateCommunityPromptsUI();
                    }
                };
                buttonPanel.Children.Add(activateBtn);
            }

            var removeBtn = new Button
            {
                Content = "×",
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = prompt.Id,
                ToolTip = "Remove"
            };
            removeBtn.Click += (s, e) =>
            {
                if (s is Button btn && btn.Tag is string promptId)
                {
                    App.CommunityPrompts?.RemovePrompt(promptId);
                    UpdateCommunityPromptsUI();
                }
            };
            buttonPanel.Children.Add(removeBtn);

            Grid.SetColumn(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            return grid;
        }

        /// <summary>
        /// Handles clicking on a companion card to switch companions.
        /// Also switches the avatar to match the selected companion.
        /// </summary>
        private void CompanionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // If the click came from the personality button, ignore it (let the button handle it)
            if (e.OriginalSource is FrameworkElement source)
            {
                // Check if clicked element or any of its parents is a personality button
                var parent = source;
                while (parent != null)
                {
                    if (parent is Button btn && btn.Name != null && btn.Name.Contains("Personality"))
                    {
                        App.Logger?.Information("Click originated from personality button, ignoring card click");
                        return; // Don't handle card click, let button handle it
                    }
                    parent = System.Windows.Media.VisualTreeHelper.GetParent(parent) as FrameworkElement;
                }
            }

            if (sender is not FrameworkElement element || element.Tag == null) return;
            if (!int.TryParse(element.Tag.ToString(), out int companionIndex)) return;

            var companionId = (Models.CompanionId)companionIndex;
            var def = Models.CompanionDefinition.GetById(companionId);

            // Switch companion
            if (App.Companion?.SwitchCompanion(companionId) == true)
            {
                UpdateCompanionCardsUI();

                // Also switch the avatar to match the companion
                _avatarTubeWindow?.SwitchToCompanionAvatar(companionId);

                App.Logger?.Information("Switched to companion: {Name}", def.Name);
            }
        }

        /// <summary>
        /// Handles clicking the personality button on a companion card.
        /// Opens a dialog to assign a prompt JSON to this companion.
        /// </summary>
        private void BtnCompanionPersonality_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            App.Logger?.Information("Personality button clicked");
            e.Handled = true; // Prevent card click from also triggering

            if (sender is not FrameworkElement element || element.Tag == null)
            {
                App.Logger?.Warning("Personality button: sender or tag is null");
                return;
            }
            if (!int.TryParse(element.Tag.ToString(), out int companionIndex))
            {
                App.Logger?.Warning("Personality button: failed to parse index");
                return;
            }

            var companionId = (Models.CompanionId)companionIndex;
            var def = Models.CompanionDefinition.GetById(companionId);
            var isUnlocked = App.Companion?.IsCompanionUnlocked(companionId) ?? false;

            App.Logger?.Information("Personality clicked: {Companion}, unlocked: {Unlocked}", def.Name, isUnlocked);

            // Check if companion is unlocked
            if (!isUnlocked)
            {
                App.Logger?.Warning("{Companion} is locked", def.Name);
                ShowStyledDialog(Loc.Get("dialog_locked"), Loc.GetF("msg_companion_locked", def.Name), "OK", "");
                return;
            }

            // Show options: Import JSON, Choose from installed, or Clear
            var currentPromptId = App.Settings?.Current?.GetCompanionPromptId(companionIndex);
            var currentPromptName = Services.CompanionService.GetAssignedPromptName(companionId);
            var hasAssigned = !string.IsNullOrEmpty(currentPromptName);

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select AI Personality for {def.Name}",
                Filter = "Prompt JSON files (*.json)|*.json",
                DefaultExt = ".json"
            };

            // Check for prompts folder
            var promptsFolder = System.IO.Path.Combine(App.EffectiveAssetsPath, "prompts");
            if (System.IO.Directory.Exists(promptsFolder))
            {
                dialog.InitialDirectory = promptsFolder;
            }

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Import the prompt file if needed
                    var prompt = App.CommunityPrompts?.ImportFromFile(dialog.FileName);
                    if (prompt != null)
                    {
                        // Assign to companion
                        App.Settings?.Current?.SetCompanionPromptId(companionIndex, prompt.Id);
                        App.Settings?.Save();

                        // Update UI
                        UpdateCompanionPromptLabels();

                        App.Logger?.Information("Assigned prompt '{Prompt}' to companion {Companion}",
                            prompt.Name, def.Name);

                        ShowStyledDialog(Loc.Get("title_personality_assigned"),
                            Loc.GetF("msg_personality_assigned", def.Name, prompt.Name),
                            Loc.Get("btn_ok"), "");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to assign prompt to companion");
                    ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt", ex.Message), Loc.Get("btn_ok"), "");
                }
            }
        }

        /// <summary>
        /// Updates the prompt labels on all companion cards.
        /// </summary>
        private void UpdateCompanionPromptLabels()
        {
            var promptTexts = new[] { TxtCompanion0Prompt, TxtCompanion1Prompt, TxtCompanion2Prompt, TxtCompanion3Prompt, TxtCompanion4Prompt };

            for (int i = 0; i < promptTexts.Length; i++)
            {
                var promptName = Services.CompanionService.GetAssignedPromptName((Models.CompanionId)i);
                var displayName = App.Mods?.MakeModAware(promptName ?? "") ?? promptName ?? "";
                promptTexts[i].Text = displayName;
                promptTexts[i].ToolTip = string.IsNullOrEmpty(displayName) ? null : Loc.GetF("tooltip_ai_personality", displayName);
            }
        }




        private void PopulateAchievementGrid()
        {
            if (AchievementGrid == null) return;
            
            AchievementGrid.Children.Clear();
            PatronAchievementGrid?.Children.Clear();
            _achievementImages.Clear();

            var tileStyle = FindResource("AchievementTile") as Style;

            // Add all achievements (patron-exclusive ones routed to the separate grid)
            foreach (var kvp in Models.Achievement.All)
            {
                var achievement = kvp.Value;
                // Skip parked achievements (no reachable unlock path in this build).
                if (achievement.IsHidden) continue;
                var isUnlocked = App.Achievements?.Progress.IsUnlocked(achievement.Id) ?? false;
                
                var border = new Border { Style = tileStyle };
                var achName = App.Mods?.MakeModAware(achievement.Name) ?? achievement.Name;
                var achFlavor = App.Mods?.MakeModAware(achievement.FlavorText) ?? achievement.FlavorText;
                var achReq = App.Mods?.MakeModAware(achievement.Requirement) ?? achievement.Requirement;
                border.ToolTip = isUnlocked
                    ? $"{achName}\n\n\"{achFlavor}\""
                    : $"???\n\nRequirement: {achReq}";

                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    Source = LoadAchievementImage(achievement.ImageName)
                };

                // Apply blur if locked
                if (!isUnlocked)
                {
                    image.Effect = new BlurEffect { Radius = 15 };
                }

                border.Child = image;

                if (achievement.IsExclusive)
                    PatronAchievementGrid?.Children.Add(border);
                else
                    AchievementGrid.Children.Add(border);

                // Store reference for later updates
                _achievementImages[achievement.Id] = image;
            }
            
            // Note: All placeholders have been replaced with real achievements
            
            UpdateAchievementCount();
            App.Logger?.Information("Achievement grid populated with {Count} achievements", _achievementImages.Count);
        }
        
        private BitmapImage? LoadAchievementImage(string imageName)
        {
            try
            {
                var image = Services.ModResourceResolver.ResolveImage($"achievements/{imageName}");
                return image as BitmapImage ?? new BitmapImage(new Uri($"pack://application:,,,/Resources/achievements/{imageName}", UriKind.Absolute));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load achievement image {Name}: {Error}", imageName, ex.Message);
                return null;
            }
        }
        
        private void RefreshAchievementTile(string achievementId)
        {
            if (!_achievementImages.TryGetValue(achievementId, out var image)) return;

            var isUnlocked = App.Achievements?.Progress.IsUnlocked(achievementId) ?? false;

            // Update blur
            image.Effect = isUnlocked ? null : new BlurEffect { Radius = 15 };

            // Update tooltip
            if (Models.Achievement.All.TryGetValue(achievementId, out var achievement))
            {
                var parent = image.Parent as Border;
                if (parent != null)
                {
                    var achName2 = App.Mods?.MakeModAware(achievement.Name) ?? achievement.Name;
                    var achFlavor2 = App.Mods?.MakeModAware(achievement.FlavorText) ?? achievement.FlavorText;
                    var achReq2 = App.Mods?.MakeModAware(achievement.Requirement) ?? achievement.Requirement;
                    parent.ToolTip = isUnlocked
                        ? $"{achName2}\n\n\"{achFlavor2}\""
                        : $"???\n\nRequirement: {achReq2}";
                }
            }

            UpdateAchievementCount();
        }

        private void RefreshAllAchievementTiles()
        {
            // Refresh all achievement tiles to reflect current unlock state
            foreach (var achievementId in _achievementImages.Keys.ToList())
            {
                RefreshAchievementTile(achievementId);
            }
            App.Logger?.Debug("All achievement tiles refreshed");
        }

        private void OnAchievementUnlockedInMainWindow(object? sender, Models.Achievement achievement)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshAchievementTile(achievement.Id);
                App.Logger?.Information("Achievement tile refreshed: {Name}", achievement.Name);
            });
        }

        #endregion
    }
}
