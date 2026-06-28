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
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;

using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the session-completion celebration window.
/// </summary>
public partial class SessionCompleteWindow : Window
{
    private readonly ILogger<SessionCompleteWindow> _logger;
    private readonly IDialogService? _dialogService;


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

        _logger = App.Services.GetRequiredService<ILogger<SessionCompleteWindow>>();
        _dialogService = App.Services?.GetService<IDialogService>();
}

    public SessionCompleteWindow(SessionLog log, bool playSound = true)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<SessionCompleteWindow>>();
        _dialogService = App.Services?.GetService<IDialogService>();
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
            var relative = CardImages[_random.Next(CardImages.Length)];
            var resourcePath = relative.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase)
                ? relative.Substring("Resources/".Length)
                : relative;

            var bitmap = AvaloniaBitmapHelper.LoadResource(resourcePath);
            if (bitmap != null)
            {
                ImgCard.Source = bitmap;
            }
            else
            {
                CardBorder.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load session completion card");
            CardBorder.IsVisible = false;
        }
    }

    private void PlayCompletionSound()
    {
        try
        {
            var settings = App.Services?.GetService<ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current;
            var masterVolume = (settings?.MasterVolume ?? 100) / 100.0;
            var volume = (float)(Math.Pow(masterVolume, 1.5) * 0.35);
            App.Services?.GetService<ISfxPlayer>()?.Play("lvup", volume);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to play completion sound");
        }
    }

    private async void MediaRow_Click(object? sender, RoutedEventArgs e)
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
                _logger?.LogInformation("SessionCompleteWindow: file gone, opened parent folder {Parent}", parent);
                return;
            }

            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_error"),
                    Loc.GetF("msg_file_not_found_with_path", path),
                    DialogSeverity.Info);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SessionCompleteWindow: failed to open file location {Path}", path);
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
