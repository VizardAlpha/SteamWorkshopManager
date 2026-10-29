using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SteamWorkshopManager.Services.Platforms.Modio;

/// <summary>
/// Zips a content folder for mod.io, which only takes zip files. OS metadata is left out and
/// entry names always use forward slashes, so the archive extracts cleanly on mod.io's servers.
/// </summary>
public static class ModioContentArchive
{
    private static readonly string[] JunkFileNames = [".DS_Store", "Thumbs.db", "desktop.ini"];

    public static void Create(string contentFolder, string zipPath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(contentFolder, "*", SearchOption.AllDirectories))
        {
            var entryName = Path.GetRelativePath(contentFolder, file).Replace('\\', '/');
            if (IsOsJunk(entryName)) continue;
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }

    /// <summary>True for OS metadata that shouldn't ship inside a mod.</summary>
    internal static bool IsOsJunk(string entryName)
    {
        foreach (var segment in entryName.Split('/'))
        {
            // macOS sidecar folder and AppleDouble resource forks (._foo).
            if (segment.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) return true;
            if (segment.StartsWith("._", StringComparison.Ordinal)) return true;
        }

        return JunkFileNames.Contains(Path.GetFileName(entryName), StringComparer.OrdinalIgnoreCase);
    }
}
