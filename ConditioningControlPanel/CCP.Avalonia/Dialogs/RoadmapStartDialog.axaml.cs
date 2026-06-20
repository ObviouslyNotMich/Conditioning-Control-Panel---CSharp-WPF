using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Themed dialog for starting a roadmap step and selecting a photo.
/// </summary>
public partial class RoadmapStartDialog : Window
{
    private readonly IDialogService? _dialogService;

    public bool Confirmed { get; private set; }
    public string? PhotoPath { get; private set; }

    public RoadmapStartDialog()
    {
        InitializeComponent();
        _dialogService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService(typeof(IDialogService)) as IDialogService;
    }

    public RoadmapStartDialog(RoadmapStepDefinition stepDef) : this()
    {
        TxtStepIcon.Text = stepDef.StepType == RoadmapStepType.Boss ? "\uD83C\uDFC6" : "\uD83D\uDCF7";

        TxtStepTitle.Text = stepDef.StepType == RoadmapStepType.Boss
            ? $"BOSS: {stepDef.Title}"
            : $"Step {stepDef.StepNumber}: {stepDef.Title}";

        TxtObjective.Text = stepDef.Objective;
        TxtPhotoRequirement.Text = stepDef.PhotoRequirement;
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private async void BtnStart_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogService == null)
        {
            MessageBoxStub.Show(
                "File picker is not available in this build.",
                "Roadmap",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var files = await _dialogService.ShowOpenFileDialogAsync(
            "Select Photo",
            new List<FileFilter>
            {
                new("Images", new[] { "png", "jpg", "jpeg", "gif", "bmp", "webp" })
            });

        if (files.Count > 0)
        {
            PhotoPath = files[0];
            Confirmed = true;
            Close(true);
        }
    }
}
