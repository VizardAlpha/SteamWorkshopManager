using System.Threading.Tasks;

namespace WorkshopManager.Services.UI;

/// <summary>
/// What the app hands over to the OS: browser, file manager, clipboard.
/// Nothing here throws; a false return means it could not be done.
/// </summary>
public interface IShellLauncher
{
    /// <summary>Opens an absolute http(s) URL in the default browser; anything else is refused.</summary>
    bool OpenUrl(string? url);

    /// <summary>Opens an existing folder in the file manager.</summary>
    bool OpenFolder(string? folder);

    /// <summary>Opens the file manager on a file's folder, with the file selected where the OS allows it.</summary>
    bool RevealFile(string? filePath);

    Task<bool> CopyTextAsync(string? text);
}
