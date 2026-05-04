using System.Collections.Generic;
using System.Threading.Tasks;

namespace SteamWorkshopManager.Services.UI;

public interface IFileDialogService
{
    Task<string?> OpenFolderAsync(string title);
    Task<string?> OpenFileAsync(string title, params string[] filters);
    Task<IReadOnlyList<string>> OpenFilesAsync(string title, params string[] filters);
    Task<string?> SaveFileAsync(string title, string defaultFileName, params string[] filters);
}
