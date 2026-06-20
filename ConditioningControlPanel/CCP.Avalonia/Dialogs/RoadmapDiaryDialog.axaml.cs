using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Roadmap;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Diary dialog showing full photo, stats, and an editable note for a completed step.
/// </summary>
public partial class RoadmapDiaryDialog : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private readonly string _stepId;
    private readonly RoadmapStepProgress _progress;
    private readonly IRoadmapService _roadmap;

    public RoadmapDiaryDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_stepId = "";
        _progress = new RoadmapStepProgress();
        _roadmap = App.Services.GetRequiredService<IRoadmapService>();
    }

    public RoadmapDiaryDialog(string stepId, RoadmapStepDefinition stepDef, RoadmapStepProgress progress) : this()
    {
        _stepId = stepId;
        _progress = progress;

        TxtStepNumber.Text = stepDef.StepType == RoadmapStepType.Boss
            ? $"BOSS - Step {stepDef.StepNumber}"
            : $"Step {stepDef.StepNumber}";
        TxtStepTitle.Text = stepDef.Title;
        TxtObjective.Text = stepDef.Objective;

        LoadPhoto();

        if (progress.CompletedAt.HasValue)
        {
            TxtCompletedDate.Text = progress.CompletedAt.Value.ToString("MMM d, yyyy");
            TxtCompletedTime.Text = progress.CompletedAt.Value.ToString("h:mm tt");
        }
        else
        {
            TxtCompletedDate.Text = "N/A";
            TxtCompletedTime.Text = "";
        }

        TxtTimeTaken.Text = progress.TimeToCompleteMinutes > 0
            ? $"{progress.TimeToCompleteMinutes} min"
            : "N/A";

        TxtUserNote.Text = progress.UserNote ?? "";
    }

    private void LoadPhoto()
    {
        try
        {
            if (!string.IsNullOrEmpty(_progress.PhotoPath))
            {
                var fullPath = _roadmap.GetFullPhotoPath(_progress.PhotoPath);
                if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                {
                    using var stream = File.OpenRead(fullPath);
                    ImgFullPhoto.Source = new Bitmap(stream);
                    TxtNoPhoto.IsVisible = false;
                    return;
                }
            }

            ImgFullPhoto.Source = null;
            TxtNoPhoto.IsVisible = true;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to load diary photo");
            ImgFullPhoto.Source = null;
            TxtNoPhoto.IsVisible = true;
        }
    }

    private async void BtnSaveNote_Click(object? sender, RoutedEventArgs e)
    {
        var newNote = TxtUserNote.Text?.Trim();

        // TODO: IRoadmapService does not yet expose UpdateStepNote.
        // Stub the persistence and provide visual feedback only.
        _progress.UserNote = newNote;
        _logger?.Information("TODO: persist updated note for step {StepId}", _stepId);

        if (sender is Button btn)
        {
            var originalContent = btn.Content;
            btn.Content = Loc.Get("btn_saved");
            btn.IsEnabled = false;

            var timer =
new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                btn.Content = originalContent;
                btn.IsEnabled = true;
            };
            timer.Start();
        }

        await Task.CompletedTask;
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
