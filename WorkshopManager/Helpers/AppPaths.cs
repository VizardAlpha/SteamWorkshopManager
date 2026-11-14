using System;
using System.IO;

namespace WorkshopManager.Helpers;

/// <summary>
/// Single source of truth for every on-disk location the app writes to.
/// Each service used to rebuild its own <c>Path.Combine(SpecialFolder.ApplicationData,
/// "SteamWorkshopManager", ...)</c>; renaming the data folder or moving to an
/// OS-specific layout (XDG, ~/Library/Application Support, etc.) used to mean
/// chasing 15 call sites. They all go through here now.
///
/// <see cref="LocalRoot"/> is intentionally separate: logs sit under
/// <c>LocalApplicationData</c> per Windows convention (machine-local, not
/// roamed) while persistent app state stays in <c>ApplicationData</c>.
/// </summary>
public static class AppPaths
{
    public const string AppName = "WorkshopManager";

    /// <summary>Folder name before 1.9.0; such folders are renamed on first access.</summary>
    public const string LegacyAppName = "SteamWorkshopManager";

    public static string Root { get; } = ResolveAppFolder(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static string LocalRoot { get; } = ResolveAppFolder(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string Sessions { get; } = Path.Combine(Root, "sessions");
    public static string Bundle { get; } = Path.Combine(Root, "bundle");
    public static string Workshop { get; } = Path.Combine(Root, "workshop");
    public static string Drafts { get; } = Path.Combine(Root, "tempo");

    /// <summary>Fetched platform data (see <c>CacheStore</c>), in each OS's own cache folder.</summary>
    public static string CacheRoot { get; } = ResolveCacheRoot();

    /// <summary>Per-feature cache folders used before 1.9.0, deleted on startup.</summary>
    public static string[] LegacyCacheFolders { get; } =
    [
        Path.Combine(Root, "cache"),
        Path.Combine(LocalRoot, "cache", "thumbnails"),
    ];

    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");
    public static string TelemetryStateFile { get; } = Path.Combine(Root, "telemetry.json");

    /// <summary>Steam login tokens. Machine-local (never roamed), DPAPI-encrypted on Windows, 0600 elsewhere.</summary>
    public static string CredentialsFile { get; } = Path.Combine(LocalRoot, "credentials.bin");

    public static string ModioCredentialsFile { get; } = Path.Combine(LocalRoot, "modio-credentials.bin");

    /// <summary>Downloaded and extracted update packages.</summary>
    public static string Updates { get; } = Path.Combine(LocalRoot, "updates");

    /// <summary>Steam Workshop's native loader reads the AppId from a file
    /// next to the binary. <see cref="AppContext.BaseDirectory"/> is where
    /// <c>SteamAPI_Init</c> looks first.</summary>
    public static string SteamAppIdFile { get; } = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");

    public static string SessionFile(string sessionId) => Path.Combine(Sessions, $"{sessionId}.json");
    public static string WorkshopForApp(uint appId) => Path.Combine(Workshop, appId.ToString());
    /// <summary>Backups of the user's own mods for one Steam game: one zip per mod, latest version.</summary>
    public static string WorkshopBackups(uint appId) => Path.Combine(WorkshopForApp(appId), BackupsFolderName);
    public const string BackupsFolderName = "backups";
    /// <summary>Downloaded mod.io files of one game, apart from the Steam AppId folders.</summary>
    public static string WorkshopForModioGame(uint gameId) => Path.Combine(Workshop, $"modio-{gameId}");
    public static string ModioFileFolder(uint gameId, string sanitizedModName, long fileId) =>
        Path.Combine(WorkshopForModioGame(gameId), $"{sanitizedModName}_{fileId}");

    public static string WorkshopVersionFolder(uint appId, string sanitizedModName, long timestamp) =>
        Path.Combine(WorkshopForApp(appId), $"{sanitizedModName}_{timestamp}");

    /// <summary>Scratch directory for the rebuild path of preview ops, alive for one
    /// Steam UGC update. Per-user on purpose: a shared /tmp parent could be pre-created by another user.</summary>
    public static string TempPreviewDir() => Path.Combine(TempRoot, "previews", Guid.NewGuid().ToString("N"));

    /// <summary>Fresh folder for a mod.io upload zip, deleted once the file is sent.</summary>
    public static string TempUploadDir() => Path.Combine(TempRoot, "uploads", Guid.NewGuid().ToString("N"));

    /// <summary>Parent of every scratch folder; swept at startup by <see cref="TempFiles"/>.</summary>
    public static string TempRoot => Path.Combine(LocalRoot, "tmp");

    private static string ResolveCacheRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(LocalRoot, "cache");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
            return ResolveAppFolder(Path.Combine(home, "Library", "Caches"));

        // XDG spec: a relative value must be ignored.
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var baseDir = !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(home, ".cache");
        return ResolveAppFolder(baseDir);
    }

    /// <summary>
    /// The app's folder under <paramref name="parent"/>. A folder left by a version before the
    /// rename is moved to the new name; if it can't be (still in use), the app keeps working from it.
    /// </summary>
    internal static string ResolveAppFolder(string parent)
    {
        var current = Path.Combine(parent, AppName);
        var legacy = Path.Combine(parent, LegacyAppName);
        if (Directory.Exists(current) || !Directory.Exists(legacy)) return current;

        try
        {
            Directory.Move(legacy, current);
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another instance may have just moved it.
            return Directory.Exists(current) ? current : legacy;
        }
    }
}
