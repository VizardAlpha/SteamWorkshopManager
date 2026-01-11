using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace SteamWorkshopManager.Services;

public class FileDialogService : IFileDialogService
{
    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public async Task<string?> OpenFolderAsync(string title)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> OpenFileAsync(string title, params string[] filters)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var fileTypeFilter = new List<FilePickerFileType>();
        foreach (var filter in filters)
        {
            fileTypeFilter.Add(new FilePickerFileType(filter)
            {
                Patterns = [$"*{filter}"]
            });
        }

        if (fileTypeFilter.Count == 0)
        {
            fileTypeFilter.Add(FilePickerFileTypes.All);
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypeFilter
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> SaveFileAsync(string title, string defaultFileName, params string[] filters)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var fileTypeChoices = new List<FilePickerFileType>();
        foreach (var filter in filters)
        {
            fileTypeChoices.Add(new FilePickerFileType(filter)
            {
                Patterns = [$"*{filter}"]
            });
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            FileTypeChoices = fileTypeChoices.Count > 0 ? fileTypeChoices : null
        });

        return file?.Path.LocalPath;
    }
}
