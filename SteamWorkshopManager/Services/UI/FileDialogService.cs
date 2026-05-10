using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace SteamWorkshopManager.Services.UI;

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

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = BuildFileTypeFilter(filters),
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, params string[] filters)
    {
        var window = GetMainWindow();
        if (window is null) return [];

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = BuildFileTypeFilter(filters),
        });

        var paths = new List<string>(files.Count);
        foreach (var f in files)
            paths.Add(f.Path.LocalPath);
        return paths;
    }

    /// <summary>
    /// Collapses the caller's extension list into a single "Files" entry that
    /// matches all of them at once, then appends "All files (*.*)". This
    /// avoids the prior UX where every extension showed up as its own filter
    /// row (".png", ".jpg", ".jpeg", ".gif") and the user had to click the
    /// dropdown for each one.
    /// </summary>
    private static List<FilePickerFileType> BuildFileTypeFilter(string[] extensions)
    {
        var result = new List<FilePickerFileType>();
        if (extensions.Length > 0)
        {
            var patterns = new List<string>(extensions.Length);
            foreach (var ext in extensions)
                patterns.Add($"*{ext}");
            result.Add(new FilePickerFileType("Files") { Patterns = patterns });
        }
        result.Add(FilePickerFileTypes.All);
        return result;
    }

}
