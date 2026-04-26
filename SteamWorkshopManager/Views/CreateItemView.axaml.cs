using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using SteamWorkshopManager.ViewModels;

namespace SteamWorkshopManager.Views;

public partial class CreateItemView : UserControl
{
    public CreateItemView()
    {
        InitializeComponent();
    }

    private void DropZone_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void DropZone_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not CreateItemViewModel vm) return;
        if (e.DataTransfer is null) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        foreach (var item in files)
        {
            if (item is not IStorageFolder folder) continue;
            var path = folder.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                vm.HandleFolderDrop(path);
                break;
            }
        }
    }
}
