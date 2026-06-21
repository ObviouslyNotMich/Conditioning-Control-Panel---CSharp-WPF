using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the session-completion celebration window.
/// </summary>
public partial class SessionCompleteWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private static readonly Random _random = new();

    private static readonly string[] CardImages = new[]
    {
        "Resources/Cards/fireworks.png",
        "Resources/Cards/hearth.png",
        "Resources/Cards/spotlight.png"
    };

    public bool? DialogResult { get; set; }

    public SessionCompleteWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
}

    public SessionCompleteWindow(SessionLog log, bool playSound = true)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
LoadRandomCard();
        ApplyLog(log);

        if (playSound && log.Completed)
        {
            PlayCompletionSound();
        }
    }

    // Back-compat: callers that don't have a SessionLog (legacy paths) can still
    // build the dialog from raw fields. Renders with an empty media list.
    public SessionCompleteWindow(Session session, TimeSpan duration, int xpEarned)
        : this(BuildLogFromLegacy(session, duration, xpEarned))
    {
    }

    private static SessionLog BuildLogFromLegacy(Session session, TimeSpan duration, int xpEarned)
    {
        return new SessionLog
        {
            SessionId = session?.Id ?? "",
            SessionName = session?.Name ?? "",
            SessionIcon = session?.Icon ?? "",
            SessionDifficulty = session?.Difficulty ?? SessionDifficulty.Easy,
            Duration = duration,
            XPEarned = xpEarned,
            Completed = true,
        };
    }

    private void ApplyLog(SessionLog log)
    {
        // Header
        if (log.Completed)
        {
            TxtMainMessage.Text = log.SessionId == "gamer_girl"
                ? Loc.Get("label_gg_good_girl")
                : Loc.Get("label_good_girl_3");
            TxtSubMessage.Text = $"{log.SessionIcon} {log.SessionName} {Loc.Get("label_completed")}".Trim();
        }
        else
        {
            TxtMainMessage.Text = Loc.Get("label_session_ended_early");
            TxtSubMessage.Text = $"{log.SessionIcon} {log.SessionName}".Trim();
            // No XP for aborted sessions - hide that column.
            XpPanel.IsVisible = false;
        }

        // Stats
        TxtSessionName.Text = log.SessionName;
        TxtDuration.Text = $"{log.Duration.Minutes:D2}:{log.Duration.Seconds:D2}";
        TxtXP.Text = $"+{log.XPEarned}";
        TxtXP.Foreground = log.SessionDifficulty switch
        {
            SessionDifficulty.Easy => new SolidColorBrush(Color.FromRgb(144, 238, 144)),
            SessionDifficulty.Medium => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
            SessionDifficulty.Hard => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
            SessionDifficulty.Extreme => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
            _ => new SolidColorBrush(Color.FromRgb(144, 238, 144))
        };

        // Media list - newest entries last (chronological order matches the session timeline).
        var rows = (log.Media ?? new List<MediaLogEntry>())
            .Select(m => new MediaRow(m))
            .ToList();

        if (rows.Count == 0)
        {
            TxtNoMedia.IsVisible = true;
            MediaList.IsVisible = false;
            TxtMediaCount.Text = "";
        }
        else
        {
            TxtNoMedia.IsVisible = false;
            MediaList.IsVisible = true;
            MediaList.ItemsSource = rows;
            int videoCount = rows.Count(r => r.Type == MediaType.Video);
            int imageCount = rows.Count - videoCount;
            TxtMediaCount.Text = Loc.GetF("label_media_count_videos_images", videoCount, imageCount);
        }
    }

    private void LoadRandomCard()
    {
        try
        {
            var env = App.Services?.GetService<IAppEnvironment>();
            var assetLoader = App.Services?.GetService<IAssetLoader>();
            var relative = CardImages[_random.Next(CardImages.Length)];

            // Prefer file on disk; fall back to embedded asset loader.
            var filePath = env != null ? Path.Combine(env.BaseDirectory, relative.Replace('/', Path.DirectorySeparatorChar)) : relative;
            if (File.Exists(filePath))
            {
                using var stream = File.OpenRead(filePath);
                ImgCard.Source = new Bitmap(stream);
            }
            else if (assetLoader != null)
            {
                var uri = new Uri($"avares://CCP.Avalonia/Assets/{relative}");
                if (assetLoader.Exists(uri))
                {
                    using var stream = assetLoader.Open(uri);
                    ImgCard.Source = new Bitmap(stream);
                }
                else
                {
                    CardBorder.IsVisible = false;
                }
            }
            else
            {
                CardBorder.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to load session completion card");
            CardBorder.IsVisible = false;
        }
    }

    private void PlayCompletionSound()
    {
        try
        {
            var env = App.Services?.GetService<IAppEnvironment>();
            var player = App.Services?.GetService<IAudioPlayer>();
            if (player == null || env == null) return;

            var soundPaths = new[]
            {
                Path.Combine(env.BaseDirectory, "Resources", "sounds", "lvup.mp3"),
                Path.Combine(env.BaseDirectory, "Resources", "lvlup.mp3"),
                Path.Combine(env.BaseDirectory, "assets", "sounds", "lvlup.mp3"),
            };

            var soundPath = soundPaths.FirstOrDefault(File.Exists);
            if (soundPath == null) return;

            var settings = App.Services?.GetService<ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current;
            var masterVolume = (settings?.MasterVolume ?? 100) / 100.0;
            var curvedVolume = Math.Pow(masterVolume, 1.5) * 0.35;
            player.SetVolume(Math.Clamp(curvedVolume, 0.01, 1.0));

            _ = Task.Run(async () =>
            {
                try { await player.PlayAsync(soundPath); }
                catch (Exception ex) { _logger?.Information("Failed to play completion sound: {Error}", ex.Message); }
            });
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to play completion sound");
        }
    }

    private void MediaRow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var path = btn.Tag as string;
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            if (File.Exists(path))
            {
                var caps = App.Services?.GetService<IPlatformCapabilities>();
                if (caps?.IsWindows == true)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
                return;
            }

            // File missing: try to open the parent folder if it still exists.
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                var caps = App.Services?.GetService<IPlatformCapabilities>();
                if (caps?.IsWindows == true)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parent}\"") { UseShellExecute = true });
                }
                _logger?.Information("SessionCompleteWindow: file gone, opened parent folder {Parent}", parent);
                return;
            }

            MessageBoxStub.Show(
                Loc.GetF("msg_file_not_found_with_path", path),
                Loc.Get("title_error"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "SessionCompleteWindow: failed to open file location {Path}", path);
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close(true);
    }

    // View model for a single row in the media list.
    private class MediaRow
    {
        public string DisplayName { get; }
        public string FilePath { get; }
        public string TimeOffsetText { get; }
        public string TypeLabel { get; }
        public IBrush TypeBrush { get; }
        public MediaType Type { get; }

        public MediaRow(MediaLogEntry entry)
        {
            Type = entry.Type;
            FilePath = entry.FilePath ?? "";
            DisplayName = !string.IsNullOrEmpty(entry.DisplayName)
                ? entry.DisplayName
                : (string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileName(FilePath));

            var t =
entry.SessionTime;
            TimeOffsetText = t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes:D2}:{t.Seconds:D2}";

            if (entry.Type == MediaType.Video)
            {
                TypeLabel = Loc.Get("label_video");
                TypeBrush = new SolidColorBrush(Color.FromRgb(255, 105, 180)); // pink
            }
            else
            {
                TypeLabel = Loc.Get("label_image");
                TypeBrush = new SolidColorBrush(Color.FromRgb(135, 206, 250)); // light blue
            }
        }
    }
}
