using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class PresetsTabView : UserControl
{
    public PresetsTabView()
    {
        AvaloniaXamlLoader.Load(this);

        if (this.FindControl<Border>("SessionDropZone") is { } dropZone)
        {
            DragDrop.SetAllowDrop(dropZone, true);
            dropZone.AddHandler(DragDrop.DragOverEvent, SessionDropZone_DragOver);
            dropZone.AddHandler(DragDrop.DragLeaveEvent, SessionDropZone_DragLeave);
            dropZone.AddHandler(DragDrop.DropEvent, SessionDropZone_Drop);
        }
    }

    private PresetsTabViewModel? ViewModel => DataContext as PresetsTabViewModel;

    private void SessionRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Session session })
        {
            ViewModel?.SelectSessionCommand.Execute(session);
        }
    }

    private void SessionDropZone_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        ViewModel?.SetDropZoneActiveCommand.Execute(true);
        e.Handled = true;
    }

    private void SessionDropZone_DragLeave(object? sender, DragEventArgs e)
    {
        ViewModel?.SetDropZoneActiveCommand.Execute(false);
        e.Handled = true;
    }

    private void SessionDropZone_Drop(object? sender, DragEventArgs e)
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
            ViewModel?.HandleSessionDropCommand.Execute(files);
        }

        e.Handled = true;
    }
}
