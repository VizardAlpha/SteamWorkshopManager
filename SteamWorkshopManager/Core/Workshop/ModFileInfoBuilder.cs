using System;
using System.IO;
using System.Linq;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>
/// Snapshot of a file or folder's content fingerprint, used as the "before"
/// reference for change detection on subsequent saves.
/// </summary>
public sealed record FileFingerprint(long Size, DateTime LastModifiedUtc)
{
    public static FileFingerprint Empty { get; } = new(0, DateTime.MinValue);
}

/// <summary>
/// File/folder inspection used by Create + Update flows. Pulled out of the
/// view-models so the change-detection math is shared and testable.
/// </summary>
public static class ModFileInfoBuilder
{
    public static FileFingerprint InspectFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return FileFingerprint.Empty;
        try
        {
            var fi = new FileInfo(filePath);
            return new FileFingerprint(fi.Length, fi.LastWriteTimeUtc);
        }
        catch
        {
            return FileFingerprint.Empty;
        }
    }

    public static FileFingerprint InspectFolder(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return FileFingerprint.Empty;
        try
        {
            var dirInfo = new DirectoryInfo(folderPath);
            var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
            var totalSize = files.Sum(f => f.Length);
            var lastModified = files.Length > 0 ? files.Max(f => f.LastWriteTimeUtc) : dirInfo.LastWriteTimeUtc;
            return new FileFingerprint(totalSize, lastModified);
        }
        catch
        {
            return FileFingerprint.Empty;
        }
    }

    /// <summary>Builds an <see cref="ItemFileInfo"/> from a file path.
    /// Returns null when the file is missing so callers don't persist noise.</summary>
    public static ItemFileInfo? BuildForFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var fp = InspectFile(path);
        return new ItemFileInfo { Path = path, Size = fp.Size, LastModifiedUtc = fp.LastModifiedUtc };
    }

    public static ItemFileInfo? BuildForFolder(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return null;
        var fp = InspectFolder(path);
        return new ItemFileInfo { Path = path, Size = fp.Size, LastModifiedUtc = fp.LastModifiedUtc };
    }
}
