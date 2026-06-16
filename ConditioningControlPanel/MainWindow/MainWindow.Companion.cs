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
    // Companion: avatar tube window management and companion event handlers.
    public partial class MainWindow
    {
        #region Companion Events (v5.3)

        private void OnCompanionXPAwarded(object? sender, (Models.CompanionId Companion, double Amount, double Modifier) args)
        {
            // Update companion progress UI in real-time when XP is earned
            Dispatcher.Invoke(() =>
            {
                // Only update if we're on the companion tab to avoid unnecessary work
                if (CompanionTab.Visibility == Visibility.Visible)
                {
                    UpdateCompanionCardsUI();
                }
            });
        }

        private void OnCompanionLevelUp(object? sender, (Models.CompanionId Companion, int NewLevel) args)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateCompanionCardsUI();

                // Mod-aware name so themed mods (e.g. Circe's Lock) don't surface the
                // Bambi roster name in the level-up notification (#325 — BUG-GLMA287TET).
                var rawCompanionName = Models.CompanionDefinition.GetById(args.Companion).Name;
                var companionName = App.Mods?.MakeModAware(rawCompanionName) ?? rawCompanionName;

                // Show notification for companion level up
                if (args.NewLevel == Models.CompanionProgress.MaxLevel)
                {
                    _trayIcon?.ShowNotification("MAX LEVEL!",
                        $"{companionName} has reached maximum level!",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                else if (args.NewLevel % 10 == 0)
                {
                    _trayIcon?.ShowNotification("Companion Level Up!",
                        $"{companionName} reached Level {args.NewLevel}!",
                        System.Windows.Forms.ToolTipIcon.Info);
                }

                // Play level up sound for significant milestones
                if (args.NewLevel % 10 == 0 || args.NewLevel == Models.CompanionProgress.MaxLevel)
                {
                    PlayLevelUpSound();
                }
            });
        }

        private void OnCompanionXPDrained(object? sender, double amount)
        {
            // Update UI when Brain Parasite drains player XP. Flash is fired regardless
            // of which tab is visible — animating an offscreen Border is harmless.
            Dispatcher.Invoke(() =>
            {
                UpdateLevelDisplay();
                FlashXpBarOnDrain();
                if (CompanionTab.Visibility == Visibility.Visible)
                {
                    UpdateCompanionCardsUI();
                }
            });
        }

        /// <summary>
        /// Pulses pink overlays on every XP bar each time Brain Parasite drains.
        /// Hits both the always-visible header XPBar and the Companion-tab hero bar so the
        /// player sees the alert wherever they are. Each overlay is sized to match only the
        /// filled (acquired) portion of its bar so the flash sits on the colored fill, not
        /// the empty track.
        /// </summary>
        private void FlashXpBarOnDrain()
        {
            App.Logger?.Information("XP drain flash fired");
            // Header bar overlay width is data-bound to XPBar.Width in XAML, no sync needed.
            FlashOverlay(XPBarFlashOverlay);

            // Companion bar is a templated ProgressBar — compute the filled width at flash time.
            if (CompanionTab.PrgCompanion0FlashOverlay != null && CompanionTab.PrgCompanion0 != null && CompanionTab.PrgCompanion0.ActualWidth > 0)
            {
                var max = Math.Max(1.0, CompanionTab.PrgCompanion0.Maximum);
                var pct = Math.Max(0.0, Math.Min(1.0, CompanionTab.PrgCompanion0.Value / max));
                CompanionTab.PrgCompanion0FlashOverlay.Width = pct * CompanionTab.PrgCompanion0.ActualWidth;
            }
            FlashOverlay(CompanionTab.PrgCompanion0FlashOverlay);
        }

        private static void FlashOverlay(System.Windows.Controls.Border? overlay)
        {
            if (overlay == null) return;
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(250),
                AutoReverse = true,
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            overlay.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void OnCompanionSwitched(object? sender, Models.CompanionId newCompanion)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateCompanionCardsUI();
            });
        }

        #endregion

        #region Avatar Tube Window

        private void InitializeAvatarTube()
        {
            // Prevent duplicate initialization
            if (_avatarTubeWindow != null)
            {
                App.Logger?.Warning("InitializeAvatarTube called but window already exists");
                return;
            }

            try
            {
                _avatarTubeWindow = new AvatarTubeWindow(this);
                App.AvatarWindow = _avatarTubeWindow; // Set global reference for services

                // Restore saved mute state
                if (App.Settings?.Current?.AvatarMuted == true)
                {
                    _avatarTubeWindow.SetMuteAvatar(true);
                }

                // Only show if main window is visible and not minimized
                if (IsVisible && WindowState != WindowState.Minimized)
                {
                    _avatarTubeWindow.Show();
                    _avatarTubeWindow.StartPoseAnimation();
                }

                App.Logger?.Information("Avatar Tube Window initialized");
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to initialize Avatar Tube Window: {Error}", ex.Message);
            }
        }

        public void ShowAvatarTube()
        {
            if (!App.Settings.Current.AvatarEnabled) return;

            // Recreate if closed
            if (_avatarTubeWindow == null)
            {
                InitializeAvatarTube();
            }
            else
            {
                _avatarTubeWindow.ShowTube();
                _avatarTubeWindow.StartPoseAnimation();
                // Force the pair above the main window. Callers reach ShowAvatarTube right
                // after a Topmost true→false pulse (panic restore, video end, chat-from-tray),
                // which lifts main to the top of the z-band. Activate() hasn't transferred
                // foreground yet, so the gated raise inside ShowTube can bail and leave the
                // tube/bubble buried behind main. A forced raise here closes that gap.
                _avatarTubeWindow.RaiseAttachedTubeAboveOwner();
            }
        }

        public void HideAvatarTube()
        {
            if (_avatarTubeWindow != null)
            {
                // Don't hide detached tube when main window minimizes — let it float independently
                // But if user disabled avatar entirely, always hide regardless of detach state
                if (_avatarTubeWindow.IsDetached && App.Settings.Current.AvatarEnabled)
                {
                    return;
                }

                _avatarTubeWindow.StopPoseAnimation();
                _avatarTubeWindow.HideTube();
            }
        }

        /// <summary>
        /// Shows only the avatar tube in detached mode (floating independently)
        /// Called from tray icon "Wake Bambi Up!" option
        /// </summary>
        public void WakeBambiUp()
        {
            // Create tube if needed
            if (_avatarTubeWindow == null)
            {
                InitializeAvatarTube();
            }

            if (_avatarTubeWindow != null)
            {
                // Show the tube
                _avatarTubeWindow.Show();
                _avatarTubeWindow.StartPoseAnimation();

                // Detach it so it floats independently
                if (!_avatarTubeWindow.IsDetached)
                {
                    _avatarTubeWindow.Detach();
                }

                _avatarTubeWindow.Giggle("Good morning~!");
            }
        }

        public void SetAvatarPose(int poseNumber)
        {
            _avatarTubeWindow?.SetPose(poseNumber);
        }

        #endregion
    }
}
