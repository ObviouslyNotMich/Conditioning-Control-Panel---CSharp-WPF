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
    // Leaderboard tab: rankings display and refresh.
    public partial class MainWindow
    {
        #region Leaderboard

        private async void LeaderboardColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            // Ignore during window initialization
            if (_isLoading || TxtLeaderboardStatus == null || App.Leaderboard == null) return;

            if (e.OriginalSource is GridViewColumnHeader header && header.Content is string headerText)
            {
                // Map header text to sort field
                // In all-time mode, level column is hidden so skip level sort
                var levelSort = _leaderboardMode == "all-time" ? "xp" : "level";
                string? sortField = headerText switch
                {
                    "Rank" => levelSort,
                    "Level" => levelSort,
                    "XP" => "xp",
                    "Patreon" => "is_patreon",
                    "Name" => null, // Client-side sort
                    "Online" => null, // Client-side sort
                    "Achievements" => null, // Client-side sort
                    _ => null
                };

                if (sortField != null)
                {
                    // Fetch fresh data then sort client-side (server always returns XP order)
                    await RefreshLeaderboardAsync(sortField);
                    ApplyLeaderboardSort(sortField);
                }
                else if (headerText == "Name")
                {
                    // Client-side alphabetical sort
                    TxtLeaderboardStatus.Text = Loc.Get("label_sorting_by_name");
                    var sorted = App.Leaderboard.Entries.OrderBy(x => x.DisplayName).ToList();
                    LstLeaderboard.ItemsSource = sorted;
                    TxtLeaderboardStatus.Text = Loc.GetF("label_0_online_1_users_sorted_by_name", App.Leaderboard.OnlineUsers, App.Leaderboard.TotalUsers);
                }
                else if (headerText == "Online")
                {
                    // Client-side: online first, then by level descending
                    TxtLeaderboardStatus.Text = Loc.Get("label_sorting_by_online_status");
                    var sorted = App.Leaderboard.Entries
                        .OrderByDescending(x => x.IsOnline)
                        .ThenByDescending(x => x.Level)
                        .ToList();
                    LstLeaderboard.ItemsSource = sorted;
                    TxtLeaderboardStatus.Text = Loc.GetF("label_0_online_1_users_online_first", App.Leaderboard.OnlineUsers, App.Leaderboard.TotalUsers);
                }
                else if (headerText == "Achievements")
                {
                    // Client-side: by achievement count descending
                    TxtLeaderboardStatus.Text = Loc.Get("label_sorting_by_achievements");
                    var sorted = App.Leaderboard.Entries
                        .OrderByDescending(x => x.AchievementsCount)
                        .ToList();
                    LstLeaderboard.ItemsSource = sorted;
                    TxtLeaderboardStatus.Text = Loc.GetF("label_0_online_1_users_sorted_by_achievements", App.Leaderboard.OnlineUsers, App.Leaderboard.TotalUsers);
                }
            }
        }

        private async void BtnRefreshLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            await RefreshLeaderboardAsync();
        }

        private async void BtnLeaderboardMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string mode && mode != _leaderboardMode)
            {
                _leaderboardMode = mode;
                UpdateLeaderboardModeButtons();
                // All-time defaults to XP ranking, monthly defaults to level
                var defaultSort = mode == "all-time" ? "xp" : "level";
                await RefreshLeaderboardAsync(defaultSort);
            }
        }

        private void UpdateLeaderboardModeButtons()
        {
            try
            {
                if (BtnLeaderboardMonthly == null || BtnLeaderboardAllTime == null) return;

                var isAllTime = _leaderboardMode == "all-time";
                var gold = (Color)ColorConverter.ConvertFromString("#FFD700");
                var pink = (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4");
                var dark = Application.Current.Resources["DarkerBg"] is Color dbg ? dbg : (Color)ColorConverter.ConvertFromString("#1A1A2E");
                var inactive = Application.Current.Resources["AccentTintedBg"] is Color itbg ? itbg : (Color)ColorConverter.ConvertFromString("#352545");

                if (isAllTime)
                {
                    BtnLeaderboardMonthly.Background = new SolidColorBrush(inactive);
                    BtnLeaderboardMonthly.Foreground = new SolidColorBrush(pink);
                    BtnLeaderboardAllTime.Background = new SolidColorBrush(gold);
                    BtnLeaderboardAllTime.Foreground = new SolidColorBrush(dark);
                }
                else
                {
                    BtnLeaderboardMonthly.Background = new SolidColorBrush(pink);
                    BtnLeaderboardMonthly.Foreground = new SolidColorBrush(Colors.White);
                    BtnLeaderboardAllTime.Background = new SolidColorBrush(inactive);
                    BtnLeaderboardAllTime.Foreground = new SolidColorBrush(pink);
                }

                // Update All-Time button border color
                var allTimeBorder = BtnLeaderboardAllTime.Template?.FindName("AllTimeBorder", BtnLeaderboardAllTime) as Border;
                if (allTimeBorder != null)
                    allTimeBorder.BorderBrush = new SolidColorBrush(isAllTime ? gold : pink);

                // Apply accent theme to rows, headers, and hover colors
                ApplyLeaderboardTheme(isAllTime ? gold : pink);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error updating leaderboard mode buttons");
            }
        }

        private void ApplyLeaderboardTheme(Color accent)
        {
            if (LstLeaderboard == null) return;

            var accentBrush = new SolidColorBrush(accent);
            var hoverBrush = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
            var selectedBrush = new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B));
            var headerBgColor = Application.Current.Resources["AccentTintedBg"] is Color tbg ? tbg : (Color)ColorConverter.ConvertFromString("#352545");
            var headerHoverBgColor = Application.Current.Resources["AccentTintedBgHover"] is Color thbg ? thbg : (Color)ColorConverter.ConvertFromString("#452555");
            var headerHoverBg = new SolidColorBrush(headerHoverBgColor);
            var headerBg = new SolidColorBrush(headerBgColor);

            // Rebuild ItemContainerStyle with the new accent color
            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(ForegroundProperty, accentBrush));
            itemStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8)));
            itemStyle.Setters.Add(new Setter(MarginProperty, new Thickness(0, 2, 0, 0)));
            itemStyle.Setters.Add(new Setter(FontSizeProperty, 18.0));
            itemStyle.Setters.Add(new Setter(FontWeightProperty, FontWeights.ExtraBold));
            itemStyle.Setters.Add(new Setter(FontFamilyProperty, new FontFamily("Segoe Print")));
            itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            // OG gold-tinted row (always gold regardless of mode)
            var ogTrigger = new DataTrigger { Binding = new System.Windows.Data.Binding("IsSeason0Og"), Value = true };
            ogTrigger.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xD7, 0x00))));
            ogTrigger.Setters.Add(new Setter(BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xD7, 0x00))));
            ogTrigger.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 0, 2)));
            itemStyle.Triggers.Add(ogTrigger);

            var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(BackgroundProperty, hoverBrush));
            itemStyle.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(BackgroundProperty, selectedBrush));
            itemStyle.Triggers.Add(selectedTrigger);

            LstLeaderboard.ItemContainerStyle = itemStyle;

            // Rebuild header style in ListView.Resources
            var headerStyle = new Style(typeof(GridViewColumnHeader));
            headerStyle.Setters.Add(new Setter(BackgroundProperty, headerBg));
            headerStyle.Setters.Add(new Setter(ForegroundProperty, accentBrush));
            headerStyle.Setters.Add(new Setter(FontWeightProperty, FontWeights.ExtraBold));
            headerStyle.Setters.Add(new Setter(FontSizeProperty, 18.0));
            headerStyle.Setters.Add(new Setter(FontFamilyProperty, new FontFamily("Segoe Print")));
            headerStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(12, 10, 12, 10)));
            headerStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 1, 2)));
            headerStyle.Setters.Add(new Setter(BorderBrushProperty, accentBrush));
            headerStyle.Setters.Add(new Setter(CursorProperty, Cursors.Hand));

            var headerHoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
            headerHoverTrigger.Setters.Add(new Setter(BackgroundProperty, headerHoverBg));
            headerStyle.Triggers.Add(headerHoverTrigger);

            LstLeaderboard.Resources[typeof(GridViewColumnHeader)] = headerStyle;
        }

        private void UpdateSeasonsColumn()
        {
            try
            {
                var gridView = LstLeaderboard?.View as GridView;
                if (gridView == null || gridView.Columns.Count == 0) return;

                var isAllTime = _leaderboardMode == "all-time";

                var seasonsCol = gridView.Columns.FirstOrDefault(c => c.Header?.ToString() == "Seasons");
                if (seasonsCol != null)
                {
                    seasonsCol.Width = isAllTime ? 80 : 0;
                }

                // Hide level column in all-time mode (inconsistent after season resets)
                var levelHeader = Loc.Get("label_level");
                var levelCol = gridView.Columns.FirstOrDefault(c => c.Header?.ToString() == levelHeader);
                if (levelCol != null)
                {
                    levelCol.Width = isAllTime ? 0 : 100;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error updating Seasons column");
            }
        }

        private void BtnLeaderboardDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string discordId && !string.IsNullOrEmpty(discordId))
            {
                try
                {
                    // Use rundll32 to force opening in default browser - this bypasses app URL handlers
                    var url = $"https://discord.com/users/{discordId}";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "rundll32",
                        Arguments = $"url.dll,FileProtocolHandler {url}",
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to open Discord profile for user {DiscordId}", discordId);
                }
            }
        }

        private void LstLeaderboard_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Get the double-clicked item
            if (LstLeaderboard?.SelectedItem is Services.LeaderboardEntry entry && !string.IsNullOrEmpty(entry.DisplayName))
            {
                App.Logger?.Information("Leaderboard double-click: Opening profile for {DisplayName}", entry.DisplayName);

                // Switch to Discord tab (which contains the Profile Viewer)
                ShowTab("discord");

                // Set the search text and display the profile
                if (TxtProfileSearch != null)
                {
                    TxtProfileSearch.Text = entry.DisplayName;
                }
                SearchAndDisplayProfile(entry.DisplayName);
            }
        }

        private async Task RefreshLeaderboardAsync(string? sortBy = null)
        {
            if (App.Leaderboard == null || TxtLeaderboardStatus == null || BtnRefreshLeaderboard == null) return;

            TxtLeaderboardStatus.Text = Loc.Get("label_syncing");
            BtnRefreshLeaderboard.IsEnabled = false;

            try
            {
                // Sync local stats to cloud first so leaderboard shows latest data
                var syncEnabled = App.ProfileSync?.IsSyncEnabled == true;
                App.Logger?.Information("Leaderboard refresh: IsSyncEnabled={SyncEnabled}, ProfileSync={HasProfileSync}, Patreon={HasPatreon}, Authenticated={IsAuth}",
                    syncEnabled,
                    App.ProfileSync != null,
                    App.Patreon != null,
                    App.Patreon?.IsAuthenticated);

                if (syncEnabled)
                {
                    App.Logger?.Information("Syncing profile before leaderboard refresh...");
                    App.Achievements?.Save(); // Save any pending achievements first
                    await App.ProfileSync.SyncProfileAsync();
                    App.Logger?.Information("Profile sync completed");
                }

                TxtLeaderboardStatus.Text = Loc.Get("label_loading_2");
                var success = await App.Leaderboard.RefreshAsync(sortBy, _leaderboardMode);

                if (success)
                {
                    // Apply client-side sort (server always returns XP order from sorted set)
                    ApplyLeaderboardSort(sortBy ?? App.Leaderboard.CurrentSortBy);
                    TxtLeaderboardStatus.Text = Loc.GetF("label_0_online_1_users", App.Leaderboard.OnlineUsers, App.Leaderboard.TotalUsers);

                    // Update season flavour text based on mode
                    if (_leaderboardMode == "all-time")
                    {
                        TxtLeaderboardSeason.Text = Loc.Get("label_all_time_legends_never_die");
                        if (TxtLeaderboardSubtitle != null)
                            TxtLeaderboardSubtitle.Text = Loc.Get("label_cumulative_xp_across_all_seasons");
                    }
                    else
                    {
                        var seasonTitle = App.QuestDefinitions?.SeasonTitle;
                        if (!string.IsNullOrEmpty(seasonTitle))
                            TxtLeaderboardSeason.Text = Loc.GetF("label_0_prove_your_devotion", seasonTitle);
                        if (TxtLeaderboardSubtitle != null)
                            TxtLeaderboardSubtitle.Text = Loc.Get("label_resets_monthly_your_rank_is_everything");
                    }

                    // Show/hide Trophy Case columns based on skill unlock
                    UpdateTrophyCaseColumns();
                    // Show/hide Seasons column based on mode
                    UpdateSeasonsColumn();

                    // Bark hook: react to the user's standing when the board loads.
                    try { App.Bark?.NotifyLeaderboardViewed(App.Leaderboard.YourRank ?? 0, App.Leaderboard.TotalUsers); } catch { }
                }
                else
                {
                    TxtLeaderboardStatus.Text = App.Leaderboard.LastRefreshError ?? Loc.Get("label_failed_to_load");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error refreshing leaderboard");
                TxtLeaderboardStatus.Text = Loc.Get("label_error_loading_leaderboard");
            }
            finally
            {
                BtnRefreshLeaderboard.IsEnabled = true;
            }
        }

        private void ApplyLeaderboardSort(string sortBy)
        {
            if (App.Leaderboard?.Entries == null || LstLeaderboard == null) return;

            List<Services.LeaderboardEntry> sorted;

            if (_leaderboardMode == "all-time")
            {
                // In all-time mode, default sort by total XP earned; "level" sorts by highest_level_ever
                sorted = sortBy switch
                {
                    "level" => App.Leaderboard.Entries.OrderByDescending(x => x.HighestLevelEver).ThenByDescending(x => x.TotalXpEarned).ToList(),
                    "xp" => App.Leaderboard.Entries.OrderByDescending(x => x.TotalXpEarned).ToList(),
                    "is_patreon" => App.Leaderboard.Entries.OrderByDescending(x => x.PatreonTier).ThenByDescending(x => x.TotalXpEarned).ToList(),
                    _ => App.Leaderboard.Entries.OrderByDescending(x => x.TotalXpEarned).ToList()
                };
            }
            else
            {
                sorted = sortBy switch
                {
                    "level" => App.Leaderboard.Entries.OrderByDescending(x => x.Level).ThenByDescending(x => x.Xp).ToList(),
                    "xp" => App.Leaderboard.Entries.OrderByDescending(x => x.Xp).ToList(),
                    "is_patreon" => App.Leaderboard.Entries.OrderByDescending(x => x.PatreonTier).ThenByDescending(x => x.Level).ToList(),
                    _ => App.Leaderboard.Entries.OrderByDescending(x => x.Xp).ToList()
                };
            }

            // Re-number ranks
            for (int i = 0; i < sorted.Count; i++)
                sorted[i].Rank = i + 1;

            LstLeaderboard.ItemsSource = sorted;
        }

        /// <summary>
        /// Show or hide Trophy Case columns based on whether the skill is unlocked
        /// </summary>
        private void UpdateTrophyCaseColumns()
        {
            try
            {
                var hasTrophyCase = App.SkillTree?.HasSkill("trophy_case") == true;
                var gridView = LstLeaderboard.View as GridView;

                if (gridView != null && gridView.Columns.Count > 0)
                {
                    // Find the trophy case columns by name
                    var longestSessionCol = gridView.Columns.FirstOrDefault(c => c.Header?.ToString() == "Best Session");
                    var highestStreakCol = gridView.Columns.FirstOrDefault(c => c.Header?.ToString() == "Best Streak");

                    // Set width to 0 to hide, restore to original width to show
                    if (longestSessionCol != null)
                    {
                        longestSessionCol.Width = hasTrophyCase ? 110 : 0;
                    }

                    if (highestStreakCol != null)
                    {
                        highestStreakCol.Width = hasTrophyCase ? 100 : 0;
                    }

                    App.Logger?.Debug("Trophy Case columns visibility updated: {Visible}", hasTrophyCase);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error updating Trophy Case columns");
            }
        }

        #endregion
    }
}
