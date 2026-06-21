using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the session-log history browser.
/// The backing log service is stubbed until ISessionLogService is available in CCP.Core.
/// </summary>
public partial class SessionLogHistoryWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly ISessionLogService _sessionLog;


    public bool? DialogResult { get; set; }

    public SessionLogHistoryWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _sessionLog = App.Services.GetRequiredService<ISessionLogService>();
}

    private void Window_Loaded(object? sender, RoutedEventArgs e) => LoadLogs();

    private void LoadLogs()
    {
        var logs = _sessionLog.LoadRecentLogs();
        var rows = logs.Select(l => new HistoryRow(l)).ToList();

        if (rows.Count == 0)
        {
            TxtEmpty.IsVisible = true;
            LogList.IsVisible = false;
            TxtCount.Text = "";
        }
        else
        {
            TxtEmpty.IsVisible = false;
            LogList.IsVisible = true;
            LogList.ItemsSource = rows;
            TxtCount.Text = Loc.GetF("label_session_count", rows.Count);
        }
    }

    private void LogRow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not HistoryRow row) return;

        try
        {
            var dialog = new SessionCompleteWindow(row.Log, playSound: false);
            _ = dialog.ShowDialog<bool?>(this);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to open historical session log");
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close(true);
    }

    private class HistoryRow
    {
        public SessionLog Log { get; }
        public string Icon { get; }
        public string Name { get; }
        public string StartedText { get; }
        public string DurationText { get; }
        public string MediaText { get; }
        public string StatusText { get; }
        public IBrush StatusBrush { get; }

        public HistoryRow(SessionLog log)
        {
            Log = log;
            Icon = log.SessionIcon ?? "";
            Name = log.SessionName ?? "";
            StartedText = log.StartedAt.ToString("g");

            var d = log.Duration;
            DurationText = d.TotalHours >= 1
                ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
                : $"{d.Minutes:D2}:{d.Seconds:D2}";

            int videos = log.Media?.Count(m => m.Type == MediaType.Video) ?? 0;
            int images =
(log.Media?.Count ?? 0) - videos;
            MediaText = Loc.GetF("label_media_count_videos_images", videos, images);

            if (log.Completed)
            {
                StatusText = Loc.Get("label_completed");
                StatusBrush = new SolidColorBrush(Color.FromRgb(144, 238, 144));
            }
            else
            {
                StatusText = Loc.Get("label_aborted");
                StatusBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0));
            }
        }
    }
}
