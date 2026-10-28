using System;
using System.IO;

namespace SteamWorkshopManager.Helpers;

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
    public const string AppName = "SteamWorkshopManager";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    public static string LocalRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

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

    /// <summary>Downloaded and extracted update packages.</summary>
    public static string Updates { get; } = Path.Combine(LocalRoot, "updates");

    /// <summary>Steam Workshop's native loader reads the AppId from a file
    /// next to the binary. <see cref="AppContext.BaseDirectory"/> is where
    /// <c>SteamAPI_Init</c> looks first.</summary>
    public static string SteamAppIdFile { get; } = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");

    public static string SessionFile(string sessionId) => Path.Combine(Sessions, $"{sessionId}.json");
    public static string WorkshopForApp(uint appId) => Path.Combine(Workshop, appId.ToString());
    public static string WorkshopVersionFolder(uint appId, string sanitizedModName, long timestamp) =>
        Path.Combine(WorkshopForApp(appId), $"{sanitizedModName}_{timestamp}");

    /// <summary>Scratch directory for the rebuild path of preview ops, alive for one
    /// Steam UGC update. Per-user on purpose: a shared /tmp parent could be pre-created by another user.</summary>
    public static string TempPreviewDir() => Path.Combine(LocalRoot, "tmp", "previews", Guid.NewGuid().ToString("N"));

    private static string ResolveCacheRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(LocalRoot, "cache");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Caches", AppName);

        // XDG spec: a relative value must be ignored.
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var baseDir = !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(home, ".cache");
        return Path.Combine(baseDir, AppName);
    }
}
