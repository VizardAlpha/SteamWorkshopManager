using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// On-disk cache for Steam imagery used anywhere the app displays a game's
/// thumbnail. Two independent caches live side by side so the wide header
/// (used by the dashboard hero) never clobbers the square library icon
/// (used by the session pill and picker flyout).
///
/// Cache layout under <c>%AppData%/SteamWorkshopManager/cache/</c>:
///   <c>headers/{appId}.jpg</c> — Steam Store header (460×215), URL is
///     derived from the AppId.
///   <c>icons/{appId}.jpg</c> — Steam Library icon (square), URL must be
///     resolved through SteamKit2 PICS and passed in at the first call.
///
/// Callers check <see cref="IconCacheFilePath(uint)"/>'s existence before
/// deciding whether a PICS round-trip is needed: a cached icon means we
/// already did the lookup once and can keep serving it without the URL.
/// </summary>
public static class SteamImageCache
{
    private static readonly Logger Log = LogService.GetLogger<object>();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly string HeaderCacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager", "cache", "headers"
    );

    private static readonly string IconCacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager", "cache", "icons"
    );

    static SteamImageCache()
    {
        Directory.CreateDirectory(HeaderCacheFolder);
        Directory.CreateDirectory(IconCacheFolder);
    }

    public static string HeaderCacheFilePath(uint appId) => Path.Combine(HeaderCacheFolder, $"{appId}.jpg");

    public static string IconCacheFilePath(uint appId) => Path.Combine(IconCacheFolder, $"{appId}.jpg");

    /// <summary>
    /// Returns the Steam Store header image (wide, ~460×215).
    /// Hits the disk cache first, falls back to the Cloudflare CDN.
    /// </summary>
    public static async Task<Bitmap?> GetHeaderAsync(uint appId, bool forceDownload = false)
    {
        if (appId == 0) return null;

        var url = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";
        return await LoadOrDownloadAsync(HeaderCacheFilePath(appId), url, forceDownload);
    }

    /// <summary>
    /// Returns the Steam Library square icon. When the icon is already on
    /// disk, <paramref name="iconUrl"/> can be <c>null</c>. When the cache
    /// misses, a non-null URL is required — obtain it via
    /// <c>SteamAppMetadataService.GetIconUrlsAsync</c>.
    /// </summary>
    public static async Task<Bitmap?> GetIconAsync(uint appId, string? iconUrl, bool forceDownload = false)
    {
        if (appId == 0) return null;
        var cachePath = IconCacheFilePath(appId);

        if (!forceDownload && File.Exists(cachePath))
        {
            try
            {
                return await Task.Run(() => new Bitmap(cachePath));
            }
            catch (Exception ex)
            {
                Log.Debug($"SteamImageCache: icon cache read failed for AppId {appId}: {ex.Message}");
                try { File.Delete(cachePath); } catch { }
            }
        }

        if (string.IsNullOrEmpty(iconUrl)) return null;

        try
        {
            var bytes = await Http.GetByteArrayAsync(iconUrl);
            await File.WriteAllBytesAsync(cachePath, bytes);

            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Log.Debug($"SteamImageCache: icon fetch failed for AppId {appId}: {ex.Message}");
            return null;
        }
    }

    public static void InvalidateHeader(uint appId)
    {
        try
        {
            var path = HeaderCacheFilePath(appId);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    public static void InvalidateIcon(uint appId)
    {
        try
        {
            var path = IconCacheFilePath(appId);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static async Task<Bitmap?> LoadOrDownloadAsync(string cachePath, string url, bool forceDownload)
    {
        if (!forceDownload && File.Exists(cachePath))
        {
            try
            {
                return await Task.Run(() => new Bitmap(cachePath));
            }
            catch (Exception ex)
            {
                Log.Debug($"SteamImageCache: cache read failed for {cachePath}: {ex.Message}");
                try { File.Delete(cachePath); } catch { }
            }
        }

        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(cachePath, bytes);

            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Log.Debug($"SteamImageCache: fetch failed for {url}: {ex.Message}");
            return null;
        }
    }
}
