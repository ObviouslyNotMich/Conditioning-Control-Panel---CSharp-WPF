using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Avalonia.Services.Tutorial;
using ConditioningControlPanel.Avalonia.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Avalonia port of the new-enhancement dialog.
/// </summary>
public partial class NewEnhancementDialog : Window
{
    public string SelectedMediaType { get; private set; } = MediaTypes.Video;
    public string SelectedSource { get; private set; } = "";

    private readonly IDialogService? _dialogService;
    private readonly ILogger<NewEnhancementDialog>? _logger;
    private readonly ISettingsService? _settings;
    private readonly IModService? _mods;

    private TutorialType? _pendingPart2Tutorial;
    private TutorialOverlay? _activeTutorialOverlay;
    private bool _createClicked;

    public NewEnhancementDialog()
    {
        InitializeComponent();
        _dialogService = App.Services?.GetService<IDialogService>();
        _logger = App.Services?.GetRequiredService<ILogger<NewEnhancementDialog>>();
        _settings = App.Services?.GetService<ISettingsService>();
        _mods = App.Services?.GetService<IModService>();
        Closed += OnDialogClosed;
    }

    private void OnDialogClosed(object? sender, EventArgs e)
    {
        if (!_createClicked)
        {
            try { _activeTutorialOverlay?.Close(); } catch { }
        }
        _activeTutorialOverlay = null;
    }

    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var overlaysToRestore = new List<TutorialOverlay>();
        try
        {
            if (Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var w in desktop.Windows)
                {
                    if (w is TutorialOverlay overlay)
                    {
                        if (w.IsVisible)
                        {
                            overlay.IsVisible = false;
                            overlaysToRestore.Add(overlay);
                        }
                    }
                }
            }
        }
        catch { }

        try
        {
            var isVideo = RbVideo.IsChecked == true;
            var filters = new[]
            {
                new FileFilter(
                    isVideo ? Loc.Get("label_video_files") : Loc.Get("label_audio_files"),
                    isVideo
                        ? new[] { "mp4", "webm", "mkv", "mov", "avi", "m4v" }
                        : new[] { "mp3", "wav", "m4a", "aac", "flac", "ogg" }),
                new FileFilter(Loc.Get("label_all_files"), new[] { "*" })
            };

            var lastDir = _settings?.Current.DeeperLastDirectory;
            var files = await (_dialogService?.ShowOpenFileDialogAsync(
                Loc.Get(isVideo ? "deeper_dialog_pick_video" : "deeper_dialog_pick_audio"),
                filters,
                initialDirectory: lastDir) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

            if (files.Count > 0)
            {
                TxtSource.Text = files[0];
            }
        }
        finally
        {
            foreach (var overlay in overlaysToRestore)
            {
                try { overlay.IsVisible = true; } catch { }
            }
        }
    }

    private void BtnLocalVideoTutorial_Click(object? sender, RoutedEventArgs e)
    {
        RbVideo.IsChecked = true;
        StartInteractiveTutorial(
            TutorialType.DeeperEditorInteractiveLocalVideo,
            TutorialType.DeeperEditorInteractiveLocalVideoPart2);
    }

    private void BtnLocalAudioTutorial_Click(object? sender, RoutedEventArgs e)
    {
        RbAudio.IsChecked = true;
        StartInteractiveTutorial(
            TutorialType.DeeperEditorInteractiveLocalAudio,
            TutorialType.DeeperEditorInteractiveLocalAudioPart2);
    }

    private void BtnTryHypnoTubeTutorial_Click(object? sender, RoutedEventArgs e)
    {
        _logger?.LogInformation("HypnoTube tutorial requested");
        RbVideo.IsChecked = true;

        // Mirror the WPF behavior: pick the active mod's first TikTok-style link,
        // falling back to the canonical HypnoTube URL if none is configured.
        var url = "https://hypnotube.com";
        try
        {
            var links = _mods?.ActiveMod?.Manifest?.Browser?.DefaultVideoLinks;
            if (links != null)
            {
                foreach (var kvp in links)
                {
                    if (kvp.Key.IndexOf("tiktok", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        url = kvp.Value;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to resolve HypnoTube tutorial URL from active mod");
        }
        TxtSource.Text = url;

        try
        {
            var settings = _settings?.Current;
            if (settings != null)
            {
                settings.HasSeenDeeperHTInteractiveTutorial = true;
                _settings?.Save();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist HasSeenDeeperHTInteractiveTutorial flag");
        }

        StartInteractiveTutorial(
            TutorialType.DeeperEditorInteractiveHT,
            TutorialType.DeeperEditorInteractiveHTPart2);
    }

    private void StartInteractiveTutorial(TutorialType part1, TutorialType part2)
    {
        _pendingPart2Tutorial = part2;
        try
        {
            if (App.Tutorial == null) return;
            if (App.Tutorial.IsActive) App.Tutorial.Skip();
            App.Tutorial.Start(part1);
            try { _activeTutorialOverlay?.Close(); } catch { }
            _activeTutorialOverlay = new TutorialOverlay(this, App.Tutorial);
            _activeTutorialOverlay.Show();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to start interactive tutorial Part 1");
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void BtnCreate_Click(object? sender, RoutedEventArgs e)
    {
        var source = TxtSource.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(source))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("deeper_dialog_new_title"),
                Loc.Get("deeper_dialog_source_required"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        SelectedMediaType = RbVideo.IsChecked == true ? MediaTypes.Video : MediaTypes.Audio;
        SelectedSource = source;

        if (_pendingPart2Tutorial.HasValue)
        {
            _createClicked = true;
            TutorialEventBus.PendingPart2Tutorial = _pendingPart2Tutorial.Value;
        }

        Close(true);
    }
}
