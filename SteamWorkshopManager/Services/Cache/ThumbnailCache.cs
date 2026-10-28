using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Services.Cache;

/// <summary>
/// Item card and gallery thumbnails: at most 4 downloads at once, cached by URL,
/// decoded downscaled off the UI thread.
/// </summary>
public sealed class ThumbnailCache(ICacheStore cache)
{
    private static readonly Logger Log = LogService.GetLogger<ThumbnailCache>();

    private const int DecodeWidth = 320;

    private static readonly HttpClient Http = SteamHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(20));
    private static readonly SemaphoreSlim DownloadGate = new(4);

    public async Task<Bitmap?> GetAsync(string url, CancellationToken ct)
    {
        var key = CacheKey.Image(url);

        try
        {
            if (await Task.Run(() => TryDecodeCached(key), ct).ConfigureAwait(false) is { } cached) return cached;

            byte[] bytes;
            await DownloadGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Another card may have fetched the same URL while this one waited.
                // Already off the UI thread here (ConfigureAwait above).
                if (TryDecodeCached(key) is { } raced) return raced;
                bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            }
            finally
            {
                DownloadGate.Release();
            }

            await cache.SetFileAsync(key, bytes).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(stream, DecodeWidth);
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug($"Thumbnail failed for {url}: {ex.Message}");
            return null;
        }
    }

    private Bitmap? TryDecodeCached(CacheKey key)
    {
        if (cache.GetFilePath(key) is not { } path) return null;

        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, DecodeWidth);
        }
        catch (Exception ex)
        {
            // A corrupt file would fail forever otherwise.
            Log.Debug($"Cached thumbnail {key} unreadable: {ex.Message}");
            cache.Remove(key);
            return null;
        }
    }
}
