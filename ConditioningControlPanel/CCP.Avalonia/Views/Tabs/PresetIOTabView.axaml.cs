using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class PresetIOTabView : UserControl
{
    public PresetIOTabView()
    {
        AvaloniaXamlLoader.Load(this);

        var dropZone = this.FindControl<Border>("DropZone");
        if (dropZone != null)
        {
            DragDrop.SetAllowDrop(dropZone, true);
            dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            dropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            dropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    private PresetIOTabViewModel? ViewModel => DataContext as PresetIOTabViewModel;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        ViewModel?.SetDropZoneActiveCommand.Execute(true);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ViewModel?.SetDropZoneActiveCommand.Execute(false);
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        ViewModel?.SetDropZoneActiveCommand.Execute(false);

        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.Handled = true;
            return;
        }

        var files = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Select(f => f.Path.LocalPath).ToArray();
        if (files?.Length > 0)
        {
            ViewModel?.HandlePresetDropCommand.Execute(files);
        }

        e.Handled = true;
    }
}
