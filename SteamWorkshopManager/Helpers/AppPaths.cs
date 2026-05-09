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

    public static string CacheHeaders { get; } = Path.Combine(Root, "cache", "headers");
    public static string CacheIcons { get; } = Path.Combine(Root, "cache", "icons");
    public static string CacheTags { get; } = Path.Combine(Root, "cache", "tags");

    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");
    public static string TelemetryStateFile { get; } = Path.Combine(Root, "telemetry.json");

    /// <summary>Steam Workshop's native loader reads the AppId from a file
    /// next to the binary. <see cref="AppContext.BaseDirectory"/> is where
    /// <c>SteamAPI_Init</c> looks first.</summary>
    public static string SteamAppIdFile { get; } = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");

    public static string SessionFile(string sessionId) => Path.Combine(Sessions, $"{sessionId}.json");
    public static string HeaderForApp(uint appId) => Path.Combine(CacheHeaders, $"{appId}.jpg");
    public static string IconForApp(uint appId) => Path.Combine(CacheIcons, $"{appId}.jpg");
    public static string TagsForApp(uint appId) => Path.Combine(CacheTags, $"{appId}.json");
    public static string WorkshopForApp(uint appId) => Path.Combine(Workshop, appId.ToString());

    /// <summary>Scratch directory under the OS temp folder for the rebuild
    /// path of preview ops — files here exist only for the duration of one
    /// Steam UGC update.</summary>
    public static string TempPreviewDir() => Path.Combine(Path.GetTempPath(), AppName, "previews", Guid.NewGuid().ToString("N"));
}
