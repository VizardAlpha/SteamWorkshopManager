using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Item card thumbnails: at most 4 downloads at once, cached on disk by URL
/// (Steam changes the URL when the image changes), decoded downscaled off the UI thread.
/// </summary>
public static class ThumbnailCache
{
    private static readonly Logger Log = new(nameof(ThumbnailCache), LogService.Instance);

    private const int DecodeWidth = 320;
    private const int MaxCachedFiles = 500;

    private static readonly HttpClient Http = SteamHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(20));
    private static readonly SemaphoreSlim DownloadGate = new(4);
    private static int _pruned;

    public static async Task<Bitmap?> GetAsync(string url, CancellationToken ct)
    {
        PruneOnce();
        var path = Path.Combine(AppPaths.CacheThumbnails, HashName(url));

        try
        {
            if (!File.Exists(path))
            {
                await DownloadGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (!File.Exists(path)) await DownloadAsync(url, path, ct).ConfigureAwait(false);
                }
                finally
                {
                    DownloadGate.Release();
                }
            }

            ct.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
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
            TryDelete(path); // A corrupt file would fail forever otherwise.
            return null;
        }
    }

    private static async Task DownloadAsync(string url, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.CacheThumbnails);
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
            await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    // Keeps the folder bounded: oldest files go first.
    private static void PruneOnce()
    {
        if (Interlocked.Exchange(ref _pruned, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(AppPaths.CacheThumbnails)) return;
                var files = new DirectoryInfo(AppPaths.CacheThumbnails).GetFiles();
                foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxCachedFiles))
                    TryDelete(file.FullName);
            }
            catch (Exception ex)
            {
                Log.Debug($"Thumbnail cache prune failed: {ex.Message}");
            }
        });
    }

    private static string HashName(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32].ToLowerInvariant() + ".img";

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
