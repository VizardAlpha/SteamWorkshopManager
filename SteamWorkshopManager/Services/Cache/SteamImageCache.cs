using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Services.Cache;

/// <summary>
/// Game artwork: the wide Store header (dashboard hero, add-session preview) and the square
/// library icon (session pill). The icon URL comes from PICS, so a cache miss needs it passed in.
/// </summary>
public sealed class SteamImageCache(ICacheStore cache)
{
    private static readonly Logger Log = LogService.GetLogger<SteamImageCache>();
    private static readonly HttpClient Http = SteamHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(10));

    private static CacheKey HeaderKey(uint appId) => CacheKey.SteamApp(appId, "header.jpg");
    private static CacheKey IconKey(uint appId) => CacheKey.SteamApp(appId, "icon.jpg");

    /// <summary>False means the icon URL has to be resolved through PICS first.</summary>
    public bool HasIcon(uint appId) => cache.GetFilePath(IconKey(appId)) is not null;

    public Task<Bitmap?> GetHeaderAsync(uint appId, bool forceDownload = false) =>
        appId == 0 ? Task.FromResult<Bitmap?>(null) : LoadAsync(HeaderKey(appId), SteamUrls.HeaderImage(appId), forceDownload);

    /// <summary><paramref name="iconUrl"/> may be null when the icon is already cached.</summary>
    public Task<Bitmap?> GetIconAsync(uint appId, string? iconUrl, bool forceDownload = false) =>
        appId == 0 ? Task.FromResult<Bitmap?>(null) : LoadAsync(IconKey(appId), iconUrl, forceDownload);

    private async Task<Bitmap?> LoadAsync(CacheKey key, string? url, bool forceDownload)
    {
        if (!forceDownload && cache.GetFilePath(key) is { } path)
        {
            try
            {
                return await Task.Run(() => new Bitmap(path));
            }
            catch (Exception ex)
            {
                Log.Debug($"Cached image {key} unreadable: {ex.Message}");
                cache.Remove(key);
            }
        }

        if (string.IsNullOrEmpty(url)) return null;

        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            await cache.SetFileAsync(key, bytes);
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            });
        }
        catch (Exception ex)
        {
            Log.Debug($"Image fetch failed for {url}: {ex.Message}");
            return null;
        }
    }
}
