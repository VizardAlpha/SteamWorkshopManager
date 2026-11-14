using System;
using System.IO;
using WorkshopManager.Services.Log;

namespace WorkshopManager.Helpers;

/// <summary>
/// The binaries were named SteamWorkshopManager before 1.9.0. Unzipping a newer release over
/// an old install leaves them behind, and the old shortcut would keep starting the old version.
/// </summary>
public static class LegacyBinaries
{
    private const string LegacyName = "SteamWorkshopManager";

    private static readonly Logger Log = new(nameof(LegacyBinaries), LogService.Instance);

    private static readonly string[] Suffixes = ["", ".exe", ".dll", ".pdb", ".deps.json", ".runtimeconfig.json"];

    public static void Remove() => Remove(AppContext.BaseDirectory, Environment.ProcessPath);

    internal static void Remove(string installDir, string? runningExe)
    {
        // Still running under the old name (old dev build): nothing to clean.
        if (string.Equals(Path.GetFileNameWithoutExtension(runningExe), LegacyName, StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var suffix in Suffixes)
        {
            var path = Path.Combine(installDir, LegacyName + suffix);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Debug($"Could not remove {path}: {ex.Message}");
            }
        }
    }
}
