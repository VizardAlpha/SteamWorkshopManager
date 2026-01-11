using System.Threading.Tasks;

namespace SteamWorkshopManager.Services;

public interface IFileDialogService
{
    Task<string?> OpenFolderAsync(string title);
    Task<string?> OpenFileAsync(string title, params string[] filters);
    Task<string?> SaveFileAsync(string title, string defaultFileName, params string[] filters);
}
