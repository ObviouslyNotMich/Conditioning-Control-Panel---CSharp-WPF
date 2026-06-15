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
    // Autonomy mode: autonomous companion behavior controls.
    public partial class MainWindow
    {
        #region Autonomy Mode

        private void ChkAutonomyEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkAutonomyEnabled.IsChecked ?? false;

            // If enabling for the first time, show consent dialog
            if (isEnabled && !App.Settings.Current.AutonomyConsentGiven)
            {
                var result = MessageBox.Show(
                    "AUTONOMY MODE\n\n" +
                    "This feature allows the companion to autonomously trigger effects:\n" +
                    "• Flash images\n" +
                    "• Videos (without strict mode)\n" +
                    "• Subliminal messages\n" +
                    "• Make comments\n\n" +
                    "She will act on her own within your configured intensity settings.\n\n" +
                    "You can disable this at any time. Videos triggered autonomously will NEVER use strict mode.\n\n" +
                    "Do you consent to enable Autonomy Mode?",
                    "Enable Autonomy Mode",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    App.Settings.Current.AutonomyConsentGiven = true;
                }
                else
                {
                    ChkAutonomyEnabled.IsChecked = false;
                    return;
                }
            }

            App.Settings.Current.AutonomyModeEnabled = isEnabled;

            // Start/stop autonomy service (works independently of engine!)
            // Requires Patreon + Consent
            var hasPatreon = App.Settings.Current.PatreonTier >= 1 || App.Patreon?.IsWhitelisted == true;

            if (isEnabled)
            {
                if (!hasPatreon)
                {
                    App.Logger?.Warning("Autonomy Mode enabled but Patreon access missing - service will not start");
                    MessageBox.Show(
                        "Autonomy Mode requires Patreon access.\n\n" +
                        "The setting has been saved, but the feature will not activate until you have Patreon access.",
                        "Patreon Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    App.Autonomy?.Stop();
                }
                else if (App.Settings.Current.AutonomyConsentGiven)
                {
                    App.Autonomy?.Start();
                }
            }
            else
            {
                App.Autonomy?.Stop();
            }
            App.Logger?.Information("Autonomy Mode toggled: {Enabled} (Engine running: {EngineRunning}, Patreon: {Patreon})",
                isEnabled, _isRunning, hasPatreon);

            App.Settings.Save();

            // Sync avatar menu state
            Dispatcher.BeginInvoke(() => _avatarTubeWindow?.UpdateQuickMenuState());
        }

        private void BtnAutonomyStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            var isCurrentlyEnabled = settings.AutonomyModeEnabled;

            // If starting for the first time, show consent dialog
            if (!isCurrentlyEnabled && !settings.AutonomyConsentGiven)
            {
                var result = MessageBox.Show(
                    "AUTONOMY MODE\n\n" +
                    "This feature allows the companion to autonomously trigger effects:\n" +
                    "• Flash images\n" +
                    "• Videos (without strict mode)\n" +
                    "• Subliminal messages\n" +
                    "• Make comments\n\n" +
                    "She will act on her own schedule based on your intensity setting.\n" +
                    "You can stop her at any time by clicking the Stop button.\n\n" +
                    "Do you consent to enabling Autonomy Mode?",
                    "Autonomy Mode Consent",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                settings.AutonomyConsentGiven = true;
            }

            // Toggle the mode
            settings.AutonomyModeEnabled = !isCurrentlyEnabled;
            App.Settings.Save();

            // Update button appearance
            UpdateAutonomyButtonState(!isCurrentlyEnabled);

            // Start/stop autonomy service
            if (!isCurrentlyEnabled)
            {
                App.Autonomy?.Start();
            }
            else
            {
                App.Autonomy?.Stop();
            }

            App.Logger?.Information("Autonomy Mode button toggled: {Enabled}", !isCurrentlyEnabled);

            // Sync avatar menu state
            Dispatcher.BeginInvoke(() => _avatarTubeWindow?.UpdateQuickMenuState());
        }

        private void UpdateAutonomyButtonState(bool isEnabled)
        {
            if (BtnAutonomyStartStop == null) return;

            if (isEnabled)
            {
                BtnAutonomyStartStop.Content = Loc.Get("btn_stop_2");
                BtnAutonomyStartStop.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")); // Pink
            }
            else
            {
                BtnAutonomyStartStop.Content = Loc.Get("btn_start_2");
                BtnAutonomyStartStop.Foreground = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // Light green
            }
        }

        /// <summary>
        /// Called from AvatarTubeWindow to sync the button/checkbox state when toggled from avatar menu
        /// </summary>
        public void SyncAutonomyCheckbox(bool isEnabled)
        {
            // Read the actual setting value to ensure consistency
            var actualValue = App.Settings?.Current?.AutonomyModeEnabled ?? false;
            App.Logger?.Information("MainWindow.SyncAutonomyCheckbox called with isEnabled={IsEnabled}, actualSetting={Actual}", isEnabled, actualValue);

            // Use BeginInvoke to ensure UI update happens after current operation completes
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    // Re-read the setting inside dispatcher to get the latest value
                    var settingValue = App.Settings?.Current?.AutonomyModeEnabled ?? false;

                    // Update button state
                    UpdateAutonomyButtonState(settingValue);

                    // Also update hidden checkbox for backwards compatibility
                    if (ChkAutonomyEnabled != null)
                    {
                        var wasLoading = _isLoading;
                        _isLoading = true;
                        ChkAutonomyEnabled.IsChecked = settingValue;
                        _isLoading = wasLoading;
                    }

                    App.Logger?.Information("MainWindow.SyncAutonomyCheckbox synced to {Value}", settingValue);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "MainWindow.SyncAutonomyCheckbox failed");
                }
            }));
        }

        private void SliderAutonomyIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAutonomyIntensity == null) return;
            TxtAutonomyIntensity.Text = $"{(int)e.NewValue}";
            App.Settings.Current.AutonomyIntensity = (int)e.NewValue;
            App.Settings.Save();
        }

        private void SliderAutonomyCooldown_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAutonomyCooldown == null) return;
            TxtAutonomyCooldown.Text = $"{(int)e.NewValue}s";
            App.Settings.Current.AutonomyCooldownSeconds = (int)e.NewValue;
            App.Settings.Save();
        }

        private void SliderAutonomyInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAutonomyInterval == null) return;
            TxtAutonomyInterval.Text = $"{(int)e.NewValue}s";
            App.Settings.Current.AutonomyRandomIntervalSeconds = (int)e.NewValue;
            App.Autonomy?.RefreshRandomTimer();
            App.Settings.Save();
        }

        private void ChkAutonomyIdle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyIdleTriggerEnabled = ChkAutonomyIdle.IsChecked ?? false;
            App.Autonomy?.RefreshIdleTimer();
            App.Settings.Save();
        }

        private void ChkAutonomyRandom_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyRandomTriggerEnabled = ChkAutonomyRandom.IsChecked ?? false;
            App.Autonomy?.RefreshRandomTimer();
            App.Settings.Save();
        }

        private void ChkAutonomyTimeAware_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyTimeAwareEnabled = ChkAutonomyTimeAware.IsChecked ?? false;
            App.Settings.Save();
        }

        private void ChkAutonomyBehavior_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyCanTriggerFlash = ChkAutonomyFlash.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerVideo = ChkAutonomyVideo.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerWebVideo = ChkAutonomyWebVideo.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerSubliminal = ChkAutonomySubliminal.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerBubbles = ChkAutonomyBubbles.IsChecked ?? false;
            App.Settings.Current.AutonomyCanComment = ChkAutonomyComment.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerMindWipe = ChkAutonomyMindWipe.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerLockCard = ChkAutonomyLockCard.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerSpiral = ChkAutonomySpiral.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerPinkFilter = ChkAutonomyPinkFilter.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerBouncingText = ChkAutonomyBouncingText.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerBubbleCount = ChkAutonomyBubbleCount.IsChecked ?? false;
            App.Settings.Save();
        }

        private void SliderAutonomyAnnounce_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAutonomyAnnounce == null) return;
            TxtAutonomyAnnounce.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.AutonomyAnnouncementChance = (int)e.NewValue;
            App.Settings.Save();
        }

        private void BtnTestAutonomy_Click(object sender, RoutedEventArgs e)
        {
            App.Autonomy?.TestTrigger();
        }

        private void BtnForceStartAutonomy_Click(object sender, RoutedEventArgs e)
        {
            App.Autonomy?.ForceStart();
        }

        #endregion
    }
}
