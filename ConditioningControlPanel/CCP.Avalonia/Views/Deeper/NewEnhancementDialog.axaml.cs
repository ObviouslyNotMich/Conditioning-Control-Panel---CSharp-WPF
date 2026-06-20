using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models.Deeper;
using ConditioningControlPanel.Core.Platform;
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
    private readonly IAppLogger? _logger;

    public NewEnhancementDialog()
    {
        InitializeComponent();
        _dialogService = App.Services?.GetService<IDialogService>();
        _logger = App.Services?.GetService<IAppLogger>();
    }

    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var isVideo = RbVideo.IsChecked == true;
        var filters = new[]
        {
            new FileFilter(
                isVideo ? "Video files" : "Audio files",
                isVideo
                    ? new[] { "mp4", "webm", "mkv", "mov", "avi", "m4v" }
                    : new[] { "mp3", "wav", "m4a", "aac", "flac", "ogg" }),
            new FileFilter("All files", new[] { "*" })
        };

        var files = await (_dialogService?.ShowOpenFileDialogAsync(
            Loc.Get(isVideo ? "deeper_dialog_pick_video" : "deeper_dialog_pick_audio"),
            filters) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

        if (files.Count > 0)
        {
            TxtSource.Text = files[0];
        }
    }

    private void BtnLocalVideoTutorial_Click(object? sender, RoutedEventArgs e)
    {
        _logger?.Information("Local video tutorial requested (not yet ported)");
        RbVideo.IsChecked = true;
    }

    private void BtnLocalAudioTutorial_Click(object? sender, RoutedEventArgs e)
    {
        _logger?.Information("Local audio tutorial requested (not yet ported)");
        RbAudio.IsChecked = true;
    }

    private void BtnTryHypnoTubeTutorial_Click(object? sender, RoutedEventArgs e)
    {
        _logger?.Information("HypnoTube tutorial requested (not yet ported)");
        RbVideo.IsChecked = true;
        TxtSource.Text = "https://hypnotube.com";
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
        Close(true);
    }
}
