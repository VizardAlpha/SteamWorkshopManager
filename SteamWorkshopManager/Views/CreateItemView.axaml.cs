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
    }

    private void DropZone_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void DropZone_Drop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        var fileNames = e.Data.GetFileNames();
#pragma warning restore CS0618

        if (fileNames is not null && DataContext is CreateItemViewModel vm)
        {
            foreach (var file in fileNames)
            {
                if (Directory.Exists(file))
                {
                    vm.HandleFolderDrop(file);
                    break;
                }
            }
        }
        e.Handled = true;
    }
}
