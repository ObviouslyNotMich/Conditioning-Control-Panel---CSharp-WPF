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
    // Settings load/save and feature-tutorial button handlers (nested).
    public partial class MainWindow
    {
        #region Settings Load/Save

        private void LoadSettings()
        {
            var s = App.Settings.Current;

            // Flash
            ChkFlashEnabled.IsChecked = s.FlashEnabled;
            ChkClickable.IsChecked = s.FlashClickable;
            ChkCorruption.IsChecked = s.CorruptionMode;
            ChkHydraLinked.IsChecked = s.HydraLinkedTiming;
            ChkFlashGlow.IsChecked = s.FlashGlowEnabled;
            SliderPerMin.Value = s.FlashFrequency;
            SliderImages.Value = s.SimultaneousImages;
            SliderMaxOnScreen.Value = s.HydraLimit;

            // Visuals
            SliderSize.Value = s.ImageScale;
            SliderOpacity.Value = s.FlashOpacity;
            SliderFade.Value = s.FadeDuration;
            SliderFlashDuration.Value = s.FlashDuration;
            ChkFlashAudio.IsChecked = s.FlashAudioEnabled;
            SliderFlashDuration.IsEnabled = !s.FlashAudioEnabled;
            SliderFlashDuration.Opacity = s.FlashAudioEnabled ? 0.5 : 1.0;
            
            // Set audio link state based on frequency
            _isLoading = false;
            UpdateAudioLinkState();
            _isLoading = true;

            // Video
            ChkVideoEnabled.IsChecked = s.MandatoryVideosEnabled;
            SliderPerHour.Value = s.VideosPerHour;
            ChkStrictLock.IsChecked = s.StrictLockEnabled;
            ChkMiniGameEnabled.IsChecked = s.AttentionChecksEnabled;
            SliderTargets.Value = s.AttentionDensity;
            ChkRandomizeTargets.IsChecked = s.RandomizeAttentionTargets;
            SliderDuration.Value = s.AttentionLifespan;
            SliderTargetSize.Value = s.AttentionSize;

            // Subliminals
            ChkSubliminalEnabled.IsChecked = s.SubliminalEnabled;
            SliderSubPerMin.Value = s.SubliminalFrequency;
            SliderFrames.Value = s.SubliminalDuration;
            SliderSubOpacity.Value = s.SubliminalOpacity;
            ChkAudioWhispers.IsChecked = s.SubAudioEnabled;
            SliderWhisperVol.Value = s.SubAudioVolume;

            // System
            ChkDualMon.IsChecked = s.DualMonitorEnabled;
            ChkWinStart.IsChecked = s.RunOnStartup;
            ChkVidLaunch.IsChecked = s.ForceVideoOnLaunch;
            ChkAutoRun.IsChecked = s.AutoStartEngine;
            ChkStartHidden.IsChecked = s.StartMinimized;
            ChkNoPanic.IsChecked = !s.PanicKeyEnabled;
            ChkOfflineMode.IsChecked = s.OfflineMode;
            if (ChkPerformanceMode != null) ChkPerformanceMode.IsChecked = s.PerformanceMode;
            if (ChkAutoPerformance != null) ChkAutoPerformance.IsChecked = s.AutoPerformanceMode;
            if (ChkVideoHwDecode != null) ChkVideoHwDecode.IsChecked = s.VideoHardwareDecoding;
            ChkStopEffectsOnRemoteDisconnect.IsChecked = s.StopEffectsOnRemoteDisconnect;
            if (ChkRemoteShareAvatar != null) ChkRemoteShareAvatar.IsChecked = s.RemoteShareAvatar;

            // Emote picker preset list (bound here so OnDeserialized normalization
            // has already run and the ItemsControl always sees exactly 5 entries).
            if (LstEmotePresets != null) LstEmotePresets.ItemsSource = s.RemoteEmotePresets;

            // Splash-overlay (big) picker — same source list, split into two rows
            // around the End Session button via index-keyed ListCollectionView filters.
            // Items are the SAME EmotePreset references as the small picker, so edits
            // in the small picker propagate via INotifyPropertyChanged.
            if (LstEmotePresetsBigTop != null)
            {
                var topView = new System.Windows.Data.ListCollectionView(s.RemoteEmotePresets)
                {
                    Filter = item => s.RemoteEmotePresets.IndexOf((Models.EmotePreset)item) < 3
                };
                LstEmotePresetsBigTop.ItemsSource = topView;
            }
            if (LstEmotePresetsBigBottom != null)
            {
                var bottomView = new System.Windows.Data.ListCollectionView(s.RemoteEmotePresets)
                {
                    Filter = item => s.RemoteEmotePresets.IndexOf((Models.EmotePreset)item) >= 3
                };
                LstEmotePresetsBigBottom.ItemsSource = bottomView;
            }

            // Deeper
            if (ChkEnableDeeper != null) ChkEnableDeeper.IsChecked = s.EnableDeeper;
            if (BtnDeeper != null) BtnDeeper.Visibility = s.EnableDeeper ? Visibility.Visible : Visibility.Collapsed;

            // Update UI for offline mode state (disable login buttons, browser, etc.)
            if (s.OfflineMode)
            {
                UpdateOfflineModeUI(true);
            }

            // Startup video display
            if (!string.IsNullOrEmpty(s.StartupVideoPath) && System.IO.File.Exists(s.StartupVideoPath))
            {
                TxtStartupVideo.Text = System.IO.Path.GetFileName(s.StartupVideoPath);
            }
            else
            {
                TxtStartupVideo.Text = Loc.Get("label_random");
            }

            // Audio
            SliderMaster.Value = s.MasterVolume;
            SliderVideoVolume.Value = s.VideoVolume;
            ChkAudioDuck.IsChecked = s.AudioDuckingEnabled;
            SliderDuck.Value = s.DuckingLevel;
            ChkExcludeBambiCloudDucking.IsChecked = s.ExcludeBambiCloudFromDucking;
            PopulateAudioOutputDevices();

            // Progression
            ChkSpiralEnabled.IsChecked = s.SpiralEnabled;
            SliderSpiralOpacity.Value = s.SpiralOpacity;
            ChkPinkFilterEnabled.IsChecked = s.PinkFilterEnabled;
            SliderPinkOpacity.Value = s.PinkFilterOpacity;
            ChkBubblesEnabled.IsChecked = s.BubblesEnabled;
            SliderBubbleFreq.Value = s.BubblesFrequency;
            SliderBubbleVolume.Value = s.BubblesVolume;
            ChkLockCardEnabled.IsChecked = s.LockCardEnabled;
            SliderLockCardFreq.Value = s.LockCardFrequency;
            SliderLockCardRepeats.Value = s.LockCardRepeats;
            ChkLockCardStrict.IsChecked = s.LockCardStrict;
            ChkBubbleCountEnabled.IsChecked = s.BubbleCountEnabled;
            ChkBubbleCountStrict.IsChecked = s.BubbleCountStrictLock;
            SliderBubbleCountFreq.Value = s.BubbleCountFrequency;
            TxtBubbleCountFreq.Text = s.BubbleCountFrequency.ToString();
            CmbBubbleCountDifficulty.SelectedIndex = s.BubbleCountDifficulty;
            ChkBouncingTextEnabled.IsChecked = s.BouncingTextEnabled;
            ChkBouncingTextAlwaysOnTop.IsChecked = s.BouncingTextAlwaysOnTop;

            // Mind Wipe
            ChkMindWipeEnabled.IsChecked = s.MindWipeEnabled;
            SliderMindWipeFreq.Value = s.MindWipeFrequency;
            SliderMindWipeVolume.Value = s.MindWipeVolume;
            ChkMindWipeLoop.IsChecked = s.MindWipeLoop;

            // Brain Drain
            ChkBrainDrainEnabled.IsChecked = s.BrainDrainEnabled;
            SliderBrainDrainIntensity.Value = s.BrainDrainIntensity;
            ChkBrainDrainHighRefresh.IsChecked = s.BrainDrainHighRefresh;

            // Autonomy Mode
            ChkAutonomyEnabled.IsChecked = s.AutonomyModeEnabled;
            UpdateAutonomyButtonState(s.AutonomyModeEnabled);
            SliderAutonomyIntensity.Value = s.AutonomyIntensity;
            SliderAutonomyCooldown.Value = s.AutonomyCooldownSeconds;
            SliderAutonomyInterval.Value = s.AutonomyRandomIntervalSeconds;
            ChkAutonomyIdle.IsChecked = s.AutonomyIdleTriggerEnabled;
            ChkAutonomyRandom.IsChecked = s.AutonomyRandomTriggerEnabled;
            ChkAutonomyTimeAware.IsChecked = s.AutonomyTimeAwareEnabled;
            ChkAutonomyFlash.IsChecked = s.AutonomyCanTriggerFlash;
            ChkAutonomyVideo.IsChecked = s.AutonomyCanTriggerVideo;
            ChkAutonomyWebVideo.IsChecked = s.AutonomyCanTriggerWebVideo;
            ChkAutonomySubliminal.IsChecked = s.AutonomyCanTriggerSubliminal;
            ChkAutonomyBubbles.IsChecked = s.AutonomyCanTriggerBubbles;
            ChkAutonomyComment.IsChecked = s.AutonomyCanComment;
            ChkAutonomyMindWipe.IsChecked = s.AutonomyCanTriggerMindWipe;
            ChkAutonomyLockCard.IsChecked = s.AutonomyCanTriggerLockCard;
            ChkAutonomySpiral.IsChecked = s.AutonomyCanTriggerSpiral;
            ChkAutonomyPinkFilter.IsChecked = s.AutonomyCanTriggerPinkFilter;
            ChkAutonomyBouncingText.IsChecked = s.AutonomyCanTriggerBouncingText;
            ChkAutonomyBubbleCount.IsChecked = s.AutonomyCanTriggerBubbleCount;
            SliderAutonomyAnnounce.Value = s.AutonomyAnnouncementChance;

            // Bouncing Text Size (add if not already loaded above)
            SliderBouncingTextSize.Value = s.BouncingTextSize;

            // Scheduler
            ChkSchedulerEnabled.IsChecked = s.SchedulerEnabled;
            TxtStartTime.Text = s.SchedulerStartTime;
            TxtEndTime.Text = s.SchedulerEndTime;
            ChkMon.IsChecked = s.SchedulerMonday;
            ChkTue.IsChecked = s.SchedulerTuesday;
            ChkWed.IsChecked = s.SchedulerWednesday;
            ChkThu.IsChecked = s.SchedulerThursday;
            ChkFri.IsChecked = s.SchedulerFriday;
            ChkSat.IsChecked = s.SchedulerSaturday;
            ChkSun.IsChecked = s.SchedulerSunday;
            ChkRampEnabled.IsChecked = s.IntensityRampEnabled;
            SliderRampDuration.Value = s.RampDurationMinutes;
            SliderMultiplier.Value = s.SchedulerMultiplier;
            
            // Ramp Links
            ChkRampLinkFlash.IsChecked = s.RampLinkFlashOpacity;
            ChkRampLinkSpiral.IsChecked = s.RampLinkSpiralOpacity;
            ChkRampLinkPink.IsChecked = s.RampLinkPinkFilterOpacity;
            ChkRampLinkMaster.IsChecked = s.RampLinkMasterAudio;
            ChkRampLinkSubAudio.IsChecked = s.RampLinkSubliminalAudio;
            ChkEndAtRamp.IsChecked = s.EndSessionOnRampComplete;

            // Haptics
            ChkHapticsEnabled.IsChecked = s.Haptics.Enabled;
            SliderHapticIntensity.Value = s.Haptics.GlobalIntensity * 100;

            // Set provider combo box first
            foreach (System.Windows.Controls.ComboBoxItem item in CmbHapticProvider.Items)
            {
                if (item.Tag?.ToString() == s.Haptics.Provider.ToString())
                {
                    CmbHapticProvider.SelectedItem = item;
                    break;
                }
            }

            // Then set URL based on provider
            TxtHapticUrl.Text = s.Haptics.Provider switch
            {
                Services.Haptics.HapticProviderType.Lovense => s.Haptics.LovenseUrl,
                Services.Haptics.HapticProviderType.Buttplug => s.Haptics.ButtplugUrl,
                _ => s.Haptics.LovenseUrl
            };

            // Set hint text based on provider
            TxtHapticUrlHint.Text = s.Haptics.Provider switch
            {
                Services.Haptics.HapticProviderType.Lovense => "Lovense: Enter IP from Lovense Remote → Settings → Game Mode (http://IP:30010)",
                Services.Haptics.HapticProviderType.Buttplug => "Buttplug: Start Intiface Central, use default ws://localhost:12345",
                _ => "Lovense: Enter IP from Lovense Remote → Settings → Game Mode (http://IP:30010)"
            };

            // Auto-connect setting
            ChkHapticAutoConnect.IsChecked = s.Haptics.AutoConnect;

            // Per-feature haptic settings
            ChkHapticBubble.IsChecked = s.Haptics.BubblePopEnabled;
            SliderHapticBubble.Value = s.Haptics.BubblePopIntensity * 100;
            ChkHapticFlashDisplay.IsChecked = s.Haptics.FlashDisplayEnabled;
            SliderHapticFlashDisplay.Value = s.Haptics.FlashDisplayIntensity * 100;
            ChkHapticFlashClick.IsChecked = s.Haptics.FlashClickEnabled;
            SliderHapticFlashClick.Value = s.Haptics.FlashClickIntensity * 100;
            ChkHapticVideo.IsChecked = s.Haptics.VideoEnabled;
            SliderHapticVideo.Value = s.Haptics.VideoIntensity * 100;
            ChkHapticTargetHit.IsChecked = s.Haptics.TargetHitEnabled;
            SliderHapticTargetHit.Value = s.Haptics.TargetHitIntensity * 100;
            ChkHapticSubliminal.IsChecked = s.Haptics.SubliminalEnabled;
            SliderHapticSubliminal.Value = s.Haptics.SubliminalIntensity * 100;
            ChkHapticLevelUp.IsChecked = s.Haptics.LevelUpEnabled;
            SliderHapticLevelUp.Value = s.Haptics.LevelUpIntensity * 100;
            ChkHapticAchievement.IsChecked = s.Haptics.AchievementEnabled;
            SliderHapticAchievement.Value = s.Haptics.AchievementIntensity * 100;
            ChkHapticBouncingText.IsChecked = s.Haptics.BouncingTextEnabled;
            SliderHapticBouncingText.Value = s.Haptics.BouncingTextIntensity * 100;

            // Per-feature haptic mode dropdowns
            CmbHapticBubbleMode.SelectedIndex = (int)s.Haptics.BubblePopMode;
            CmbHapticFlashDisplayMode.SelectedIndex = (int)s.Haptics.FlashDisplayMode;
            CmbHapticFlashClickMode.SelectedIndex = (int)s.Haptics.FlashClickMode;
            CmbHapticVideoMode.SelectedIndex = (int)s.Haptics.VideoMode;
            CmbHapticTargetHitMode.SelectedIndex = (int)s.Haptics.TargetHitMode;
            CmbHapticSubliminalMode.SelectedIndex = (int)s.Haptics.SubliminalMode;
            CmbHapticLevelUpMode.SelectedIndex = (int)s.Haptics.LevelUpMode;
            CmbHapticAchievementMode.SelectedIndex = (int)s.Haptics.AchievementMode;
            CmbHapticBouncingTextMode.SelectedIndex = (int)s.Haptics.BouncingTextMode;

            // Keyword Triggers
            {
                SliderKeywordBufferTimeout.Value = s.KeywordBufferTimeoutMs;
                SliderKeywordGlobalCooldown.Value = s.KeywordGlobalCooldownSeconds;
                SliderKeywordSessionMultiplier.Value = s.KeywordSessionMultiplier;

                var hasKeywordAccess = KeywordTriggerService.HasAccess();

                // Show/hide lock indicator
                if (TxtKeywordTriggersLocked != null)
                    TxtKeywordTriggersLocked.Visibility = hasKeywordAccess ? Visibility.Collapsed : Visibility.Visible;
                if (BtnKeywordTriggersStartStop != null)
                    BtnKeywordTriggersStartStop.IsEnabled = hasKeywordAccess;

                UpdateKeywordTriggersButtonState();
                RefreshKeywordTriggerList();

                // Screen OCR
                if (ChkScreenOcrEnabled != null)
                {
                    ChkScreenOcrEnabled.IsChecked = s.ScreenOcrEnabled;
                    ChkScreenOcrEnabled.IsEnabled = hasKeywordAccess;
                    SliderScreenOcrInterval.Value = s.ScreenOcrIntervalMs / 1000.0;
                    ScreenOcrIntervalPanel.Visibility = s.ScreenOcrEnabled && hasKeywordAccess ? Visibility.Visible : Visibility.Collapsed;
                    if (CmbOcrConfirmation != null)
                        CmbOcrConfirmation.SelectedIndex = Math.Clamp(s.OcrConfirmationScans - 1, 0, 2);
                }
                if (ChkKeywordHighlightEnabled != null)
                {
                    ChkKeywordHighlightEnabled.IsChecked = s.KeywordHighlightEnabled;
                    if (HighlightDurationPanel != null)
                    {
                        HighlightDurationPanel.Visibility = s.KeywordHighlightEnabled ? Visibility.Visible : Visibility.Collapsed;
                        SliderKeywordHighlightDuration.Value = s.KeywordHighlightDurationMs / 1000.0;
                        TxtKeywordHighlightDuration.Text = $"{s.KeywordHighlightDurationMs / 1000.0:0.0}s";
                        if (CmbOcrHighlightMode != null)
                            CmbOcrHighlightMode.SelectedIndex = s.OcrHighlightAll ? 0 : 1;
                        if (ChkHighlightVisibleInCapture != null)
                            ChkHighlightVisibleInCapture.IsChecked = s.OcrHighlightVisibleInCapture;
                    }
                }
            }

            // Discord Sharing Settings
            if (ChkDiscordTabShowOnline != null) ChkDiscordTabShowOnline.IsChecked = s.ShowOnlineStatus;

            // Update Discord UI (both main tab and Patreon tab)
            UpdateQuickDiscordUI();

            // Update level display
            UpdateLevelDisplay();

            // Update all slider text displays
            UpdateSliderTexts();

            // Start autonomy service if it was enabled (works independently of engine)
            var hasPatreonAccess = s.PatreonTier >= 1 || App.Patreon?.IsWhitelisted == true;
            if (hasPatreonAccess && s.AutonomyModeEnabled && s.AutonomyConsentGiven)
            {
                App.Autonomy?.Start();
                App.Logger?.Debug("MainWindow: Started autonomy service on settings load");
            }
        }

        /// <summary>
        /// Updates all slider text displays to match current slider values
        /// Called after loading settings since the value changed events are suppressed during load
        /// </summary>
        private void UpdateSliderTexts()
        {
            // Flash sliders
            if (TxtPerMin != null) TxtPerMin.Text = ((int)SliderPerMin.Value).ToString();
            if (TxtImages != null) TxtImages.Text = ((int)SliderImages.Value).ToString();
            if (TxtMaxOnScreen != null) TxtMaxOnScreen.Text = ((int)SliderMaxOnScreen.Value).ToString();
            if (TxtSize != null) TxtSize.Text = $"{(int)SliderSize.Value}%";
            if (TxtOpacity != null) TxtOpacity.Text = $"{(int)SliderOpacity.Value}%";
            if (TxtFade != null) TxtFade.Text = $"{(int)SliderFade.Value}%";
            
            // Video sliders
            if (TxtPerHour != null) TxtPerHour.Text = ((int)SliderPerHour.Value).ToString();
            if (TxtTargets != null) TxtTargets.Text = ((int)SliderTargets.Value).ToString();
            if (TxtDuration != null) TxtDuration.Text = $"{(int)SliderDuration.Value}s";
            if (TxtTargetSize != null) TxtTargetSize.Text = $"{(int)SliderTargetSize.Value}px";
            
            // Subliminal sliders
            if (TxtSubPerMin != null) TxtSubPerMin.Text = ((int)SliderSubPerMin.Value).ToString();
            if (TxtFrames != null) TxtFrames.Text = ((int)SliderFrames.Value).ToString();
            if (TxtSubOpacity != null) TxtSubOpacity.Text = $"{(int)SliderSubOpacity.Value}%";
            if (TxtWhisperVol != null) TxtWhisperVol.Text = $"{(int)SliderWhisperVol.Value}%";
            
            // Audio sliders
            if (TxtMaster != null) TxtMaster.Text = $"{(int)SliderMaster.Value}%";
            if (TxtVideoVolume != null) TxtVideoVolume.Text = $"{(int)SliderVideoVolume.Value}%";
            if (TxtDuck != null) TxtDuck.Text = $"{(int)SliderDuck.Value}%";
            
            // Progression sliders
            if (TxtSpiralOpacity != null) TxtSpiralOpacity.Text = $"{(int)SliderSpiralOpacity.Value}%";
            if (TxtPinkOpacity != null) TxtPinkOpacity.Text = $"{(int)SliderPinkOpacity.Value}%";
            if (TxtBubbleFreq != null) TxtBubbleFreq.Text = ((int)SliderBubbleFreq.Value).ToString();
            if (TxtBubbleVolume != null) TxtBubbleVolume.Text = $"{(int)SliderBubbleVolume.Value}%";
            if (TxtLockCardFreq != null) TxtLockCardFreq.Text = ((int)SliderLockCardFreq.Value).ToString();
            if (TxtLockCardRepeats != null) TxtLockCardRepeats.Text = $"{(int)SliderLockCardRepeats.Value}x";
            if (TxtBouncingTextSize != null) TxtBouncingTextSize.Text = $"{(int)SliderBouncingTextSize.Value}%";
            if (TxtMindWipeFreq != null) TxtMindWipeFreq.Text = $"{(int)SliderMindWipeFreq.Value}/h";
            if (TxtMindWipeVolume != null) TxtMindWipeVolume.Text = $"{(int)SliderMindWipeVolume.Value}%";
            if (TxtBrainDrainIntensity != null) TxtBrainDrainIntensity.Text = $"{(int)SliderBrainDrainIntensity.Value}%";
            
            // Scheduler sliders
            if (TxtRampDuration != null) TxtRampDuration.Text = $"{(int)SliderRampDuration.Value} min";
            if (TxtMultiplier != null) TxtMultiplier.Text = $"{SliderMultiplier.Value:F1}x";

            // Haptic sliders
            if (TxtHapticIntensity != null) TxtHapticIntensity.Text = $"{(int)SliderHapticIntensity.Value}%";
            if (TxtHapticBubble != null) TxtHapticBubble.Text = $"{(int)SliderHapticBubble.Value}%";
            if (TxtHapticFlashDisplay != null) TxtHapticFlashDisplay.Text = $"{(int)SliderHapticFlashDisplay.Value}%";
            if (TxtHapticFlashClick != null) TxtHapticFlashClick.Text = $"{(int)SliderHapticFlashClick.Value}%";
            if (TxtHapticVideo != null) TxtHapticVideo.Text = $"{(int)SliderHapticVideo.Value}%";
            if (TxtHapticTargetHit != null) TxtHapticTargetHit.Text = $"{(int)SliderHapticTargetHit.Value}%";
            if (TxtHapticSubliminal != null) TxtHapticSubliminal.Text = $"{(int)SliderHapticSubliminal.Value}%";
            if (TxtHapticLevelUp != null) TxtHapticLevelUp.Text = $"{(int)SliderHapticLevelUp.Value}%";
            if (TxtHapticAchievement != null) TxtHapticAchievement.Text = $"{(int)SliderHapticAchievement.Value}%";
        }

        private void SaveSettings()
        {
            // velvet-mosaic: feature popups write to App.Settings.Current on every edit,
            // so the settings object is already the source of truth. The legacy dashboard
            // controls (now inside LegacyDashboardHost, Collapsed) can be stale. Re-sync
            // them from settings before this method reads them, otherwise stale control
            // values would clobber the popup changes.
            var wasLoading = _isLoading;
            _isLoading = true;
            try { LoadSettings(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "SaveSettings: legacy control refresh failed"); }
            finally { _isLoading = wasLoading; }

            var s = App.Settings.Current;

            // Flash
            s.FlashEnabled = ChkFlashEnabled.IsChecked ?? true;
            s.FlashClickable = ChkClickable.IsChecked ?? true;
            s.CorruptionMode = ChkCorruption.IsChecked ?? false;
            s.HydraLinkedTiming = ChkHydraLinked.IsChecked ?? true;
            s.FlashGlowEnabled = ChkFlashGlow.IsChecked ?? true;
            s.FlashFrequency = (int)SliderPerMin.Value;
            s.SimultaneousImages = (int)SliderImages.Value;
            s.HydraLimit = (int)SliderMaxOnScreen.Value;

            // Visuals
            s.ImageScale = (int)SliderSize.Value;
            s.FlashOpacity = (int)SliderOpacity.Value;
            s.FadeDuration = (int)SliderFade.Value;

            // Video
            s.MandatoryVideosEnabled = ChkVideoEnabled.IsChecked ?? false;
            s.VideosPerHour = (int)SliderPerHour.Value;
            s.StrictLockEnabled = ChkStrictLock.IsChecked ?? false;
            s.AttentionChecksEnabled = ChkMiniGameEnabled.IsChecked ?? false;
            s.AttentionDensity = (int)SliderTargets.Value;
            s.RandomizeAttentionTargets = ChkRandomizeTargets.IsChecked ?? false;
            s.AttentionLifespan = (int)SliderDuration.Value;
            s.AttentionSize = (int)SliderTargetSize.Value;

            // Subliminals
            s.SubliminalEnabled = ChkSubliminalEnabled.IsChecked ?? false;
            s.SubliminalFrequency = (int)SliderSubPerMin.Value;
            s.SubliminalDuration = (int)SliderFrames.Value;
            s.SubliminalOpacity = (int)SliderSubOpacity.Value;
            s.SubAudioEnabled = ChkAudioWhispers.IsChecked ?? false;
            s.SubAudioVolume = (int)SliderWhisperVol.Value;

            // System
            s.DualMonitorEnabled = ChkDualMon.IsChecked ?? true;
            s.RunOnStartup = ChkWinStart.IsChecked ?? false;
            s.ForceVideoOnLaunch = ChkVidLaunch.IsChecked ?? false;
            s.AutoStartEngine = ChkAutoRun.IsChecked ?? false;
            s.StartMinimized = ChkStartHidden.IsChecked ?? false;
            s.PanicKeyEnabled = !(ChkNoPanic.IsChecked ?? false);
            s.OfflineMode = ChkOfflineMode.IsChecked ?? false;
            if (ChkPerformanceMode != null) s.PerformanceMode = ChkPerformanceMode.IsChecked ?? false;
            if (ChkAutoPerformance != null) s.AutoPerformanceMode = ChkAutoPerformance.IsChecked ?? true;
            if (ChkVideoHwDecode != null) s.VideoHardwareDecoding = ChkVideoHwDecode.IsChecked ?? true;

            // Deeper
            if (ChkEnableDeeper != null) s.EnableDeeper = ChkEnableDeeper.IsChecked ?? true;

            // Audio
            s.MasterVolume = (int)SliderMaster.Value;
            s.AudioDuckingEnabled = ChkAudioDuck.IsChecked ?? true;
            s.DuckingLevel = (int)SliderDuck.Value;
            s.ExcludeBambiCloudFromDucking = ChkExcludeBambiCloudDucking.IsChecked ?? true;

            // Progression
            s.SpiralEnabled = ChkSpiralEnabled.IsChecked ?? false;
            s.SpiralOpacity = (int)SliderSpiralOpacity.Value;
            s.PinkFilterEnabled = ChkPinkFilterEnabled.IsChecked ?? false;
            s.PinkFilterOpacity = (int)SliderPinkOpacity.Value;
            s.BubblesEnabled = ChkBubblesEnabled.IsChecked ?? false;
            s.BubblesFrequency = (int)SliderBubbleFreq.Value;
            s.LockCardEnabled = ChkLockCardEnabled.IsChecked ?? false;
            s.LockCardFrequency = (int)SliderLockCardFreq.Value;
            s.LockCardRepeats = (int)SliderLockCardRepeats.Value;
            s.LockCardStrict = ChkLockCardStrict.IsChecked ?? false;

            // Brain Drain
            s.BrainDrainEnabled = ChkBrainDrainEnabled.IsChecked ?? false;
            s.BrainDrainIntensity = (int)SliderBrainDrainIntensity.Value;
            s.BrainDrainHighRefresh = ChkBrainDrainHighRefresh.IsChecked ?? false;

            // Scheduler - track if settings changed
            var schedulerWasEnabled = s.SchedulerEnabled;
            s.SchedulerEnabled = ChkSchedulerEnabled.IsChecked ?? false;
            s.SchedulerStartTime = TxtStartTime.Text;
            s.SchedulerEndTime = TxtEndTime.Text;
            s.SchedulerMonday = ChkMon.IsChecked ?? true;
            s.SchedulerTuesday = ChkTue.IsChecked ?? true;
            s.SchedulerWednesday = ChkWed.IsChecked ?? true;
            s.SchedulerThursday = ChkThu.IsChecked ?? true;
            s.SchedulerFriday = ChkFri.IsChecked ?? true;
            s.SchedulerSaturday = ChkSat.IsChecked ?? true;
            s.SchedulerSunday = ChkSun.IsChecked ?? true;

            // If scheduler was just enabled or settings changed, reset flags and check immediately
            if (s.SchedulerEnabled && !schedulerWasEnabled)
            {
                _schedulerAutoStarted = false;
                _manuallyStoppedDuringSchedule = false;
                // Check scheduler immediately after save completes
                Dispatcher.BeginInvoke(new Action(() => CheckSchedulerAfterSettingsChange()), System.Windows.Threading.DispatcherPriority.Background);
            }
            s.IntensityRampEnabled = ChkRampEnabled.IsChecked ?? false;
            s.RampDurationMinutes = (int)SliderRampDuration.Value;
            s.SchedulerMultiplier = SliderMultiplier.Value;
            
            // Ramp Links
            s.RampLinkFlashOpacity = ChkRampLinkFlash.IsChecked ?? false;
            s.RampLinkSpiralOpacity = ChkRampLinkSpiral.IsChecked ?? false;
            s.RampLinkPinkFilterOpacity = ChkRampLinkPink.IsChecked ?? false;
            s.RampLinkMasterAudio = ChkRampLinkMaster.IsChecked ?? false;
            s.RampLinkSubliminalAudio = ChkRampLinkSubAudio.IsChecked ?? false;
            s.EndSessionOnRampComplete = ChkEndAtRamp.IsChecked ?? false;

            App.Settings.Save();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // First, apply current settings to the settings object
            SaveSettings();

            // Find current preset
            var currentPresetName = App.Settings.Current.CurrentPresetName;
            var currentPreset = _allPresets.FirstOrDefault(p => p.Name == currentPresetName);

            // Determine if we should create new or overwrite
            if (currentPreset == null || currentPreset.IsDefault || string.IsNullOrEmpty(currentPresetName))
            {
                // No preset, default preset, or unknown - ask to create new
                var result = MessageBox.Show(
                    "Would you like to save your current settings as a new preset?\n\n" +
                    "This will create a custom preset that you can load later.",
                    "Save as Preset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    PromptSaveNewPreset();
                }
                else
                {
                    MessageBox.Show(Loc.Get("msg_settings_saved"), Loc.Get("title_success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // Custom user preset - ask to overwrite
                var result = MessageBox.Show(
                    $"Do you want to overwrite preset '{currentPreset.Name}' with your current settings?\n\n" +
                    "Click 'Yes' to overwrite, 'No' to save as new preset, or 'Cancel' to just save settings.",
                    "Overwrite Preset?",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Overwrite existing preset
                    var updated = Models.Preset.FromSettings(App.Settings.Current, currentPreset.Name, currentPreset.Description);
                    updated.Id = currentPreset.Id;
                    updated.CreatedAt = currentPreset.CreatedAt;

                    var index = App.Settings.Current.UserPresets.FindIndex(p => p.Id == currentPreset.Id);
                    if (index >= 0)
                    {
                        App.Settings.Current.UserPresets[index] = updated;
                        App.Settings.Save();
                        RefreshPresetsList();

                        App.Logger?.Information("Overwritten preset: {Name}", updated.Name);
                        MessageBox.Show(Loc.GetF("msg_preset_0_updated", updated.Name), Loc.Get("title_preset_saved"),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else if (result == MessageBoxResult.No)
                {
                    // Save as new preset
                    PromptSaveNewPreset();
                }
                else
                {
                    // Cancel - just show settings saved message
                    MessageBox.Show(Loc.Get("msg_settings_saved"), Loc.Get("title_success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            if (App.Lockdown?.IsActive == true)
            {
                MessageBox.Show(Loc.Get("msg_you_are_in_lockdown_mode_nthere_is_no_escape"), Loc.Get("title_lockdown"),
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (_isRunning)
            {
                var result = MessageBox.Show(Loc.Get("msg_engine_is_running_stop_and_exit"), Loc.Get("title_confirm_exit"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
                StopEngine();
            }
            _exitRequested = true;
            EnsureSessionRestoredForExit();
            SaveSettings();
            // Under ShutdownMode=OnLastWindowClose, Close()ing only the main window leaves the
            // avatar tube and pooled keep-alive overlay windows (Flash/Subliminal/Chaos) alive —
            // especially right after a Chaos run — so the app lingered headless and never reached
            // App.OnExit/Environment.Exit. Shutdown() closes ALL windows (this window still runs
            // its _exitRequested cleanup via OnClosing) and fires OnExit. Matches the tray Exit path.
            Application.Current.Shutdown();
        }

        private void BtnMainHelp_Click(object sender, RoutedEventArgs e)
        {
            // Hide browser (WebView2 doesn't respect WPF z-order)
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Hidden;
            MainTutorialOverlay.Visibility = Visibility.Visible;
        }

        private void BtnReportBug_Click(object sender, RoutedEventArgs e)
        {
            OpenBugReportWindow();
        }

        private void BtnTutorialReportBug_Click(object sender, RoutedEventArgs e)
        {
            // Close the tutorial overlay first, then open the bug report dialog
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            OpenBugReportWindow();
        }

        private void OpenBugReportWindow()
        {
            try
            {
                var dialog = new BugReportWindow { Owner = this };
                dialog.ShowDialog();
            }
            catch (System.Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open BugReportWindow");
                MessageBox.Show(this, Loc.Get("bug_report_error_toast") + "\n\n" + ex.Message,
                    Loc.Get("bug_report_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainTutorial_Close(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
        }

        private void MainTutorial_Close(object sender, MouseButtonEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
        }

        private void MainTutorial_ContentClick(object sender, MouseButtonEventArgs e)
        {
            // Prevent closing when clicking on the content
            e.Handled = true;
        }

        private TutorialOverlay? _tutorialOverlay;

        private void BtnStartTutorial_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial();
        }

        public void StartTutorial(TutorialType type = TutorialType.FullTour)
        {
            if (_tutorialOverlay != null) return;

            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Hidden;

            // Configure tutorial callbacks for tab switching
            App.Tutorial.ConfigureCallbacks(
                showSettings: () => ShowTab("settings"),
                showPresets: () => { ShowTab("presets"); RefreshPresetsList(); },
                showProgression: () => ShowTab("progression"),
                showAchievements: () => ShowTab("achievements"),
                showCompanion: () => ShowTab("companion"),
                // Exclusives tab eliminated — route tutorial's "patreon" step to the
                // App Info & Data popup which hosts the login/data sections.
                showPatreon: () => ShowAppInfoPopup(),
                showAwareness: () => ShowTab("awareness"),
                showDeeper: () => ShowTab("deeper")
            );

            App.Tutorial.Start(type);
            _tutorialOverlay = new TutorialOverlay(this, App.Tutorial);
            _tutorialOverlay.Closed += (s, e) =>
            {
                _tutorialOverlay = null;
                if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            };
            _tutorialOverlay.Show();
        }

        #region Feature Tutorial Button Handlers

        private void BtnTutorialGettingStarted_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.GettingStarted);
        }

        private void BtnTutorialSettings_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Settings);
        }

        private void BtnTutorialPresets_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Presets);
        }

        private void BtnTutorialProgression_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Progression);
        }

        private void BtnTutorialAchievements_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Achievements);
        }

        private void BtnTutorialCompanion_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Companion);
        }

        private void BtnTutorialPatreon_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Patreon);
        }

        private void BtnTutorialAvatar_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Avatar);
        }

        private void BtnTutorialAwareness_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartAwarenessTutorial();
        }

        // Same tour, but launched directly from the in-tab "Tutorial" button rather
        // than via the help-menu overlay (so we don't toggle MainTutorialOverlay).
        private void BtnAwarenessTutorial_Click(object sender, RoutedEventArgs e)
        {
            StartAwarenessTutorial();
        }

        private void BtnCompanionTutorial_Click(object sender, RoutedEventArgs e)
        {
            StartTutorial(TutorialType.Companion);
        }

        private void StartAwarenessTutorial()
        {
            // One-shot: when the Awareness tour finishes naturally (user reached the
            // last step), pop the Puppy preset editor so they have something concrete
            // to play with while the walkthrough is fresh. Skipping mid-tour does not
            // open the editor — skip means "I'm done with this".
            EventHandler? onCompleted = null;
            onCompleted = (s, args) =>
            {
                App.Tutorial.TutorialCompleted -= onCompleted;
                if (App.Tutorial.CurrentTutorialType != TutorialType.Awareness) return;
                if (App.Tutorial.CurrentStepIndex != App.Tutorial.TotalSteps - 1) return;

                try
                {
                    var puppy = App.KeywordPresets?.GetPreset("builtin.puppy");
                    if (puppy == null) return;
                    var dlg = new AwarenessPresetDetailDialog(puppy) { Owner = this };
                    dlg.Show();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Awareness tutorial editor-open failed: {Error}", ex.Message);
                }
            };
            App.Tutorial.TutorialCompleted += onCompleted;

            StartTutorial(TutorialType.Awareness);
        }

        private void BtnTutorialModding_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            var modCreator = new ModCreatorWindow(startWithTutorial: true) { Owner = this };
            modCreator.Show();
        }

        #endregion

        private void OpenLinktree()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://linktr.ee/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        #endregion
    }
}
