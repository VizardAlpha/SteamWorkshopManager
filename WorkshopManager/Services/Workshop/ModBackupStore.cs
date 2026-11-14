using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkshopManager.Models;

namespace WorkshopManager.Services.Workshop;

/// <summary>
/// Backups of the user's own mods: one zip per mod, its latest version only, in a folder the app
/// controls. The file name carries the mod's id and update time, so "already on disk" is a file
/// lookup and a newer download replaces the older zip.
/// </summary>
public static class ModBackupStore
{
    private const string Extension = ".zip";

    /// <summary>"Title_id_updateTime.zip": readable in a file manager, unique by id.</summary>
    public static string FileName(WorkshopItem item) => string.Create(CultureInfo.InvariantCulture,
        $"{WorkshopDownloadService.SanitizeModName(item.Title)}_{item.Id}_{UpdateStamp(item)}{Extension}");

    /// <summary>The backup of the mod's current version, null when it is not on disk.</summary>
    public static string? FindCurrent(string folder, WorkshopItem item) =>
        BackupsOf(folder, item).FirstOrDefault(p => IsCurrent(p, item));

    /// <summary>Free bytes on the volume holding <paramref name="folder"/>, null when it can't be read.</summary>
    public static long? FreeSpace(string folder)
    {
        try
        {
            // The folder may not exist yet: its closest existing parent is on the same volume.
            var existing = Path.GetFullPath(folder);
            while (!Directory.Exists(existing))
            {
                var parent = Path.GetDirectoryName(existing);
                if (string.IsNullOrEmpty(parent)) return null;
                existing = parent;
            }

            var drive = OperatingSystem.IsWindows() ? Path.GetPathRoot(existing) : existing;
            return string.IsNullOrEmpty(drive) ? null : new DriveInfo(drive).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stores what Steam downloaded (a folder, or a single file for old items) as the mod's backup.
    /// Content that already is one zip is copied as it is; anything else is zipped.
    /// </summary>
    public static async Task<string> StoreAsync(string source, string folder, WorkshopItem item, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, FileName(item));
        // Written as .part and renamed, so an interrupted backup never looks complete.
        var part = target + ".part";

        try
        {
            await Task.Run(() =>
            {
                File.Delete(part);
                if (SingleZip(source) is { } zip)
                    File.Copy(zip, part);
                else if (Directory.Exists(source))
                    ZipFile.CreateFromDirectory(source, part, CompressionLevel.SmallestSize, includeBaseDirectory: false);
                else
                    ZipSingleFile(source, part);

                ct.ThrowIfCancellationRequested();
                File.Move(part, target, overwrite: true);
            }, ct);
        }
        finally
        {
            try { File.Delete(part); } catch { /* best effort */ }
        }

        foreach (var older in BackupsOf(folder, item).Where(p => !IsCurrent(p, item)))
        {
            try { File.Delete(older); } catch { /* best effort */ }
        }

        return target;
    }

    private static long UpdateStamp(WorkshopItem item) =>
        new DateTimeOffset(DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static bool IsCurrent(string path, WorkshopItem item) => Path.GetFileName(path).EndsWith(
        string.Create(CultureInfo.InvariantCulture, $"_{item.Id}_{UpdateStamp(item)}{Extension}"), StringComparison.OrdinalIgnoreCase);

    /// <summary>Every backup of that mod, whatever title it had when it was made.</summary>
    private static IEnumerable<string> BackupsOf(string folder, WorkshopItem item)
    {
        if (!Directory.Exists(folder)) return [];
        var marker = string.Create(CultureInfo.InvariantCulture, $"_{item.Id}_");
        return Directory.EnumerateFiles(folder, "*" + Extension)
            .Where(p => Path.GetFileName(p).Contains(marker, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>The zip when the content is nothing but one zip file.</summary>
    private static string? SingleZip(string source)
    {
        if (File.Exists(source))
            return source.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? source : null;
        if (!Directory.Exists(source)) return null;

        var entries = Directory.EnumerateFileSystemEntries(source).Take(2).ToList();
        return entries.Count == 1 && File.Exists(entries[0]) && entries[0].EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? entries[0]
            : null;
    }

    private static void ZipSingleFile(string file, string zipPath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.SmallestSize);
    }
}
