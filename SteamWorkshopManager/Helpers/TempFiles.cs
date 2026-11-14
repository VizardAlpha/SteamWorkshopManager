using System;
using System.IO;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Helpers;

/// <summary>Scratch folders under <see cref="AppPaths.TempRoot"/> are deleted by their owner; a crash leaves them behind.</summary>
internal static class TempFiles
{
    private static readonly Logger Log = new(nameof(TempFiles), LogService.Instance);

    // Old enough that no running upload, even from a second instance, still owns the folder.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    /// <summary>Deletes scratch folders older than a day. Called once at shell startup.</summary>
    public static void PurgeStale()
    {
        try
        {
            if (!Directory.Exists(AppPaths.TempRoot)) return;

            var cutoff = DateTime.UtcNow - StaleAfter;
            foreach (var kind in Directory.EnumerateDirectories(AppPaths.TempRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(kind))
                {
                    if (Directory.GetLastWriteTimeUtc(dir) >= cutoff) continue;
                    try { Directory.Delete(dir, recursive: true); }
                    catch (Exception ex) { Log.Debug($"Could not delete stale temp folder: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Temp folder sweep failed: {ex.Message}");
        }
    }
}
