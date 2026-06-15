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
    // Lab tab: AI lab session controls and state.
    public partial class MainWindow
    {
        #region Lab

        private void InitializeLockdown()
        {
            if (App.Lockdown == null) return;

            App.Lockdown.LockdownActivated += OnLockdownActivated;
            App.Lockdown.LockdownDeactivated += OnLockdownDeactivated;
            App.Lockdown.CountdownTick += OnLockdownTick;
        }

        private void BtnActivateLockdown_Click(object sender, RoutedEventArgs e)
        {
            if (App.Lockdown == null) return;

            // Get duration from combo box
            var selectedItem = CmbLockdownDuration.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is not string minutesStr || !int.TryParse(minutesStr, out var minutes))
                return;

            var duration = TimeSpan.FromMinutes(minutes);

            // Show double warning with clear consequences
            var confirmed = WarningDialog.ShowDoubleWarning(this, "Lockdown Mode",
                "- You will be LOCKED IN for " + minutes + " minutes\n" +
                "- Strict Lock will be FORCED ON\n" +
                "- Panic Key will be DISABLED\n" +
                "- Alt+F4, Alt+Tab, Windows key, and Escape will be BLOCKED\n" +
                "- You CANNOT close or minimize the application\n" +
                "- The only escape is waiting for the timer to expire\n" +
                "  (or Ctrl+Alt+Del → Task Manager as a safety valve)");

            if (!confirmed) return;

            App.Lockdown.Activate(duration);
        }

        private void BtnStartQuiz_Click(object sender, RoutedEventArgs e)
        {
            // Prevent opening multiple quiz windows — focus existing one instead
            var existingQuiz = Application.Current.Windows.OfType<QuizWindow>().FirstOrDefault();
            if (existingQuiz != null)
            {
                existingQuiz.Activate();
                existingQuiz.Focus();
                return;
            }

            if (App.Ai == null || !App.Ai.IsAvailable)
            {
                MessageBox.Show(Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"), "Login Required",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fullscreen = ChkQuizFullscreen?.IsChecked == true;
            var playDrone = ChkQuizDrone?.IsChecked == true;
            var quizWindow = new QuizWindow(fullscreen, playDrone);
            quizWindow.Closed += (s, args) => RefreshPastQuizzes();
            quizWindow.Show();
        }

        /// <summary>
        /// Lab → Chaos Mode hero card. Opens the setup/lobby window where the user
        /// configures the run; BEGIN CHAOS there persists settings and launches via
        /// <see cref="App.Chaos"/> (which owns the countdown, HUD and loop).
        /// Modeless on purpose: ShowDialog would disable every other app window,
        /// including the loadout sidebar that opens beside the Warren.
        /// </summary>
        private void BtnStartChaos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.Chaos == null || App.Chaos.IsRunning) return;
                // Happy path run 1: the Dollhouse stays shut until the first descent is done.
                // FALL IN drops straight into the scripted naked run instead.
                if (Services.Chaos.ChaosMeta.State.RunsCompleted == 0)
                {
                    App.Chaos.StartRun(Services.Chaos.ChaosHappyPath.BuildFirstRunConfig());
                    return;
                }
                if (ChaosHubWindow.Current != null) { ChaosHubWindow.Current.Activate(); return; }
                var hub = new ChaosHubWindow { Owner = this };
                hub.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnStartChaos_Click failed");
                MessageBox.Show("Couldn't start Down the Rabbit Hole:\n\n" + ex.Message, "Down the Rabbit Hole",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Quick Start: launch a Chaos run with the saved settings, bypassing the modal hub.
        /// Mirrors what BEGIN CHAOS does after SaveToSettings (StartRun reads ChaosRunConfig.FromSettings),
        /// just without the dialog.
        /// </summary>
        private void BtnQuickStartChaos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.Chaos == null || App.Chaos.IsRunning) return;
                // Happy path run 1: the quick start drops into the same scripted naked run.
                if (Services.Chaos.ChaosMeta.State.RunsCompleted == 0)
                {
                    App.Chaos.StartRun(Services.Chaos.ChaosHappyPath.BuildFirstRunConfig());
                    return;
                }
                App.Chaos.StartRun();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnQuickStartChaos_Click failed");
                MessageBox.Show("Couldn't start Down the Rabbit Hole:\n\n" + ex.Message, "Down the Rabbit Hole",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshPastQuizzes()
        {
            try
            {
                var history = QuizService.LoadHistory();
                PastQuizzesList.Children.Clear();

                if (history.Count == 0)
                {
                    TxtPastQuizzesHeader.Visibility = Visibility.Collapsed;
                    PastQuizzesPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                TxtPastQuizzesHeader.Visibility = Visibility.Visible;
                PastQuizzesPanel.Visibility = Visibility.Visible;

                // Trend summary at top — show latest archetype + trend per category that has history
                var categories = history.Select(h => h.Category).Distinct();
                foreach (var cat in categories)
                {
                    var trend = QuizService.GetScoreTrend(history, cat);
                    if (trend == null) continue;

                    // Extract archetype from latest profile text
                    var latestEntry = history.FirstOrDefault(h => h.Category == cat);
                    var archetype = "";
                    if (latestEntry != null)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(latestEntry.ProfileText, @"You are a (.+?)\.");
                        if (match.Success) archetype = match.Groups[1].Value;
                    }

                    var arrow = trend.Direction switch
                    {
                        TrendDirection.Up => "\u2191",
                        TrendDirection.Down => "\u2193",
                        TrendDirection.Flat => "\u2192",
                        _ => ""
                    };
                    var catDisplay = latestEntry != null && !string.IsNullOrEmpty(latestEntry.CategoryName)
                        ? latestEntry.CategoryName : cat.ToString();
                    var trendLabel = trend.Direction == TrendDirection.FirstQuiz
                        ? $"{catDisplay}: {trend.LatestPercent}%"
                        : $"{catDisplay}: {trend.LatestPercent}% {arrow}{Math.Abs(trend.DeltaPercent)}%";
                    if (!string.IsNullOrEmpty(archetype))
                        trendLabel += $" · {archetype}";

                    var trendRow = new TextBlock
                    {
                        Text = trendLabel,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(8, 3, 8, 3)
                    };
                    PastQuizzesList.Children.Add(trendRow);
                }

                foreach (var entry in history)
                {
                    var pct = entry.MaxScore > 0 ? (int)Math.Round((double)entry.TotalScore / entry.MaxScore * 100) : 0;
                    var catName = !string.IsNullOrEmpty(entry.CategoryName) ? entry.CategoryName : entry.Category.ToString();
                    var label = $"{entry.TakenAt:MMM d}  ·  {catName}  ·  {entry.TotalScore}/{entry.MaxScore} ({pct}%)";

                    var row = new Border
                    {
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Padding = new Thickness(8, 5, 8, 5),
                        Background = System.Windows.Media.Brushes.Transparent
                    };

                    var txt = new TextBlock
                    {
                        Text = label,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                        FontSize = 11.5
                    };
                    row.Child = txt;

                    var captured = entry;
                    row.MouseLeftButtonDown += (s, args) =>
                    {
                        // Close any existing report window before opening a new one
                        foreach (var w in Application.Current.Windows.OfType<QuizReportWindow>().ToList())
                            w.Close();
                        new QuizReportWindow(captured) { Owner = this }.Show();
                    };
                    row.MouseEnter += (s, args) =>
                    {
                        if (s is Border b) b.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
                    };
                    row.MouseLeave += (s, args) =>
                    {
                        if (s is Border b) b.Background = System.Windows.Media.Brushes.Transparent;
                    };

                    PastQuizzesList.Children.Add(row);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MainWindow: Failed to refresh past quizzes");
            }
        }

        // ============ POP QUIZ HANDLERS ============

        private void ChkPopQuizEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current == null) return;
            App.Settings.Current.PopQuizEnabled = ChkPopQuizEnabled.IsChecked == true;
        }

        private void SliderPopQuizFrequency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (App.Settings?.Current == null || TxtPopQuizFrequency == null) return;
            var val = (int)Math.Round(e.NewValue);
            App.Settings.Current.PopQuizFrequency = val;
            TxtPopQuizFrequency.Text = $"{val}/session hr";
        }

        private void BtnTestPopQuiz_Click(object sender, RoutedEventArgs e)
        {
            App.PopQuiz?.TestPopQuiz();
        }

        // ============ WALLPAPER OVERRIDE HANDLERS ============

        private void ChkWallpaperEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current == null || App.Wallpaper == null) return;

            var enabled = ChkWallpaperEnabled.IsChecked == true;
            if (enabled)
            {
                if (!App.Wallpaper.Activate())
                {
                    // No images found — uncheck and notify
                    ChkWallpaperEnabled.IsChecked = false;
                    App.Settings.Current.WallpaperEnabled = false;
                    MessageBox.Show(Loc.Get("msg_no_wallpaper_images"), "Wallpaper Override",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                TxtCurrentWallpaper.Text = App.Wallpaper.CurrentFilename;
                TxtCurrentWallpaper.Visibility = Visibility.Visible;
                BtnShuffleWallpaper.Visibility = Visibility.Visible;
            }
            else
            {
                App.Wallpaper.Deactivate();
                TxtCurrentWallpaper.Visibility = Visibility.Collapsed;
                BtnShuffleWallpaper.Visibility = Visibility.Collapsed;
            }
            App.Settings.Current.WallpaperEnabled = enabled;
        }

        private void BtnShuffleWallpaper_Click(object sender, RoutedEventArgs e)
        {
            if (App.Wallpaper == null) return;
            App.Wallpaper.Shuffle();
            TxtCurrentWallpaper.Text = App.Wallpaper.CurrentFilename;
        }

        private void OnLockdownActivated()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Enable system key suppression on the keyboard hook
                    if (_keyboardHook != null)
                        _keyboardHook.SuppressSystemKeys = true;

                    // Gray out strict lock and panic key toggles
                    if (ChkStrictLock != null)
                    {
                        ChkStrictLock.IsEnabled = false;
                        ChkStrictLock.Opacity = 0.4;
                        ChkStrictLock.ToolTip = Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape");
                    }
                    if (ChkNoPanic != null)
                    {
                        ChkNoPanic.IsEnabled = false;
                        ChkNoPanic.Opacity = 0.4;
                        ChkNoPanic.ToolTip = Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape");
                    }

                    // Swap UI panels
                    if (LockdownSetupPanel != null) LockdownSetupPanel.Visibility = Visibility.Collapsed;
                    if (LockdownActivePanel != null) LockdownActivePanel.Visibility = Visibility.Visible;

                    // Reset secret exit state
                    _lockdownTimerClickCount = 0;
                    if (TxtLockdownExit != null)
                    {
                        TxtLockdownExit.Visibility = Visibility.Collapsed;
                        TxtLockdownExit.Text = "";
                    }

                    // Apply blood-red theme
                    ApplyLockdownTheme();

                    // Play activation flash animation
                    PlayLockdownActivationAnimation();

                    App.Logger?.Information("Lockdown UI activated");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Error activating lockdown UI");
                }
            });
        }

        private void OnLockdownDeactivated()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Disable system key suppression
                    if (_keyboardHook != null)
                        _keyboardHook.SuppressSystemKeys = false;

                    // Restore strict lock and panic key toggles
                    if (ChkStrictLock != null)
                    {
                        ChkStrictLock.IsEnabled = true;
                        ChkStrictLock.Opacity = 1.0;
                        ChkStrictLock.ToolTip = null;
                    }
                    if (ChkNoPanic != null)
                    {
                        ChkNoPanic.IsEnabled = true;
                        ChkNoPanic.Opacity = 1.0;
                        ChkNoPanic.ToolTip = null;
                    }

                    // Swap UI panels back
                    if (LockdownSetupPanel != null) LockdownSetupPanel.Visibility = Visibility.Visible;
                    if (LockdownActivePanel != null) LockdownActivePanel.Visibility = Visibility.Collapsed;

                    // Hide secret exit
                    if (TxtLockdownExit != null)
                        TxtLockdownExit.Visibility = Visibility.Collapsed;

                    // Restore normal theme
                    RestoreLockdownTheme();

                    App.Logger?.Information("Lockdown UI deactivated");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Error deactivating lockdown UI");
                }
            });
        }

        private void OnLockdownTick(TimeSpan remaining)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (TxtLockdownTimer != null)
                {
                    if (remaining.TotalHours >= 1)
                        TxtLockdownTimer.Text = remaining.ToString(@"h\:mm\:ss");
                    else
                        TxtLockdownTimer.Text = remaining.ToString(@"mm\:ss");
                }
            });
        }

        private void TxtLockdownTimer_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.Now;

            // Reset click count if more than 1 second since last click
            if ((now - _lockdownTimerLastClick).TotalMilliseconds > 1000)
                _lockdownTimerClickCount = 0;

            _lockdownTimerLastClick = now;
            _lockdownTimerClickCount++;

            if (_lockdownTimerClickCount >= 5 && TxtLockdownExit != null)
            {
                TxtLockdownExit.Visibility = Visibility.Visible;
                TxtLockdownExit.Focus();
                _lockdownTimerClickCount = 0;
            }
        }

        private void TxtLockdownExit_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (TxtLockdownExit != null)
            {
                var phrase = TxtLockdownExit.Text;
                var success = App.Lockdown?.TryExitWithPhrase(phrase) ?? false;

                if (!success)
                {
                    // Wrong phrase — clear and hide
                    TxtLockdownExit.Text = "";
                    TxtLockdownExit.Visibility = Visibility.Collapsed;
                }
            }
        }

        // --- Lockdown Theme ---

        private static readonly Color LockdownCrimson = (Color)ColorConverter.ConvertFromString("#DC143C");
        private static readonly Color LockdownDarkRed = (Color)ColorConverter.ConvertFromString("#8B0000");
        private static readonly Color LockdownPanelBg = (Color)ColorConverter.ConvertFromString("#1A0A0A");
        private static readonly Color LockdownWindowBg = (Color)ColorConverter.ConvertFromString("#100505");

        private void ApplyLockdownTheme()
        {
            try
            {
                // Save current values for restoration
                _preLockdownWindowBg = Background;
                _preLockdownTitleBarBg = TitleBarBorder?.Background;

                // Window background
                Background = new SolidColorBrush(LockdownWindowBg);

                // Title bar
                if (TitleBarBorder != null)
                    TitleBarBorder.Background = new SolidColorBrush(LockdownDarkRed);

                // Player title and glow
                if (TxtPlayerTitle != null)
                {
                    TxtPlayerTitle.Foreground = new SolidColorBrush(LockdownCrimson);
                    if (TxtPlayerTitle.Effect is DropShadowEffect glow)
                        glow.Color = LockdownCrimson;
                }

                // Header version
                if (TxtHeaderVersion != null)
                    TxtHeaderVersion.Foreground = new SolidColorBrush(LockdownCrimson);

                // Level label
                if (TxtLevelLabel != null)
                    TxtLevelLabel.Foreground = new SolidColorBrush(LockdownCrimson);

                // XP bar
                if (XPBar != null)
                    XPBar.Background = new SolidColorBrush(LockdownCrimson);

                // Banner texts
                if (TxtBannerPrimary != null)
                    TxtBannerPrimary.Foreground = new SolidColorBrush(LockdownCrimson);
                if (TxtBannerSecondary != null)
                    TxtBannerSecondary.Foreground = new SolidColorBrush(LockdownCrimson);
                if (TxtBannerTertiary != null)
                    TxtBannerTertiary.Foreground = new SolidColorBrush(LockdownCrimson);

                // Lockdown card border → red glow
                if (LockdownCardBorder != null)
                {
                    LockdownCardBorder.BorderBrush = new SolidColorBrush(LockdownCrimson);
                    LockdownCardBorder.Background = new SolidColorBrush(LockdownPanelBg);
                }

                // Update Application-level resource brushes (affects styled controls)
                var res = Application.Current.Resources;
                res["PinkBrush"] = new SolidColorBrush(LockdownCrimson);
                res["DarkPinkBrush"] = new SolidColorBrush(LockdownDarkRed);
                res["TransparentPinkBrush"] = new SolidColorBrush(Color.FromArgb(0x30, 0xDC, 0x14, 0x3C));
                res["PinkButtonHoveredBrush"] = new SolidColorBrush(LockdownCrimson);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to apply lockdown theme");
            }
        }

        private void RestoreLockdownTheme()
        {
            try
            {
                // Restore saved values
                if (_preLockdownWindowBg != null)
                    Background = _preLockdownWindowBg;
                if (_preLockdownTitleBarBg != null && TitleBarBorder != null)
                    TitleBarBorder.Background = _preLockdownTitleBarBg;

                // Restore lockdown card to normal gradient border using mod colors
                if (LockdownCardBorder != null)
                {
                    var accentHex = App.Mods?.GetAccentColorHex() ?? "#FF69B4";
                    var secondaryHex = App.Mods?.GetSecondaryColorHex() ?? "#9B59B6";
                    var accentColor = (Color)ColorConverter.ConvertFromString(accentHex);
                    var secondaryColor = (Color)ColorConverter.ConvertFromString(secondaryHex);

                    var borderBrush = new LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(1, 1)
                    };
                    borderBrush.GradientStops.Add(new GradientStop(accentColor, 0));
                    borderBrush.GradientStops.Add(new GradientStop(secondaryColor, 1));
                    LockdownCardBorder.BorderBrush = borderBrush;

                    var bgBrush = new LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(1, 1)
                    };
                    bgBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#1A1A32"), 0));
                    bgBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#201A38"), 1));
                    LockdownCardBorder.Background = bgBrush;
                }

                // Re-apply mode-aware theme colors (restores all resource brushes + named elements)
                RefreshThemeAwareElements();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to restore lockdown theme");
            }
        }

        private void PlayLockdownActivationAnimation()
        {
            try
            {
                // Create a full-screen red flash overlay
                var flash = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 220, 20, 60)), // semi-transparent crimson
                    IsHitTestVisible = false
                };

                RootGrid.Children.Add(flash);

                // Fade out over 600ms
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(600),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                fadeOut.Completed += (_, _) =>
                {
                    try { RootGrid.Children.Remove(flash); } catch { }
                };

                flash.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to play lockdown animation");
            }
        }

        #endregion
    }
}
