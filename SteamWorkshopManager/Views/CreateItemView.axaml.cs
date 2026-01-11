using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class CreateItemView : UserControl
{
    public CreateItemView()
    {
        InitializeComponent();

        var dropZone = this.FindControl<Border>("DropZone");
        if (dropZone is not null)
        {
            dropZone.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
            dropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            dropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (DataContext is CreateItemViewModel vm)
        {
            vm.IsDragOver = true;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is CreateItemViewModel vm)
        {
            vm.IsDragOver = false;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is CreateItemViewModel vm)
        {
            vm.IsDragOver = false;

#pragma warning disable CS0618 // DragEventArgs.Data is obsolete
            var files = e.Data.GetFiles();
#pragma warning restore CS0618

            if (files is not null)
            {
                foreach (var file in files)
                {
                    var path = file.Path.LocalPath;
                    if (Directory.Exists(path))
                    {
                        vm.HandleFolderDrop(path);
                        break;
                    }
                }
            }
        }
    }
}
