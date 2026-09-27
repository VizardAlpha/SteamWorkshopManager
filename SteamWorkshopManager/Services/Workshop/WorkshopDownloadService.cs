using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Services.Workshop;

[JsonSerializable(typeof(DownloadFileResponse))]
internal partial class DownloadJsonContext : JsonSerializerContext;

public class WorkshopDownloadService
{
    private static readonly Logger Log = LogService.GetLogger<WorkshopDownloadService>();
    private readonly HttpClient _httpClient;
    private readonly SessionHost _host;


    public WorkshopDownloadService(SessionHost host, HttpClient? httpClient = null)
    {
        _host = host;
        _httpClient = httpClient ?? SteamHttpClientFactory.Create();
    }

    public async Task<string?> GetDownloadUrlAsync(ulong publishedFileId, long revision, string manifestId)
    {
        try
        {
            var url = $"https://steamcommunity.com/sharedfiles/downloadfile/?id={publishedFileId}&revision={revision}&manifestid={manifestId}";
            Log.Debug($"Fetching download URL from {url}");

            string? json;

            if (SteamAuthService.IsAuthenticated)
            {
                Log.Debug("Using authenticated HttpClient for download URL request");
                var authClient = SteamAuthService.GetAuthenticatedHttpClient();
                json = await authClient.GetStringAsync(url);
            }
            else
            {
                // Anonymous Steam URL fetches go through the worker because
                // SteamHTTP (with the session's cookie jar) is only available
                // in the process that owns Steamworks.
                Log.Debug("Routing unauthenticated download URL request through worker");
                json = _host.Worker is null ? null : await _host.Worker.FetchSteamWebAsync(url);
            }

            if (json == null)
            {
                Log.Warning("Returned null for download URL request");
                return null;
            }

            var response = JsonSerializer.Deserialize(json, DownloadJsonContext.Default.DownloadFileResponse);

            if (response is { Success: 1, Url: not null })
            {
                Log.Debug($"Got download URL for file {publishedFileId} revision {revision}");
                return response.Url;
            }

            Log.Warning($"Download URL not available for file {publishedFileId} revision {revision} (success={response?.Success})");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to get download URL for file {publishedFileId}", ex);
            return null;
        }
    }

    public async Task<string?> DownloadVersionAsync(uint appId, ulong publishedFileId, string modName,
        ChangeLogEntry entry, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string? partPath = null;
        try
        {
            var downloadUrl = await GetDownloadUrlAsync(publishedFileId, entry.Timestamp, entry.ManifestId);
            if (downloadUrl == null)
                return null;

            var sanitizedName = SanitizeModName(modName);
            var versionFolder = AppPaths.WorkshopVersionFolder(appId, sanitizedName, entry.Timestamp);
            Directory.CreateDirectory(versionFolder);

            var filePath = Path.Combine(versionFolder, $"{sanitizedName}_{entry.Timestamp}.zip");
            // Written as .part and renamed on success, so a cut download never looks complete.
            partPath = filePath + ".part";

            Log.Info($"Downloading version to {filePath}");

            // Use regular HttpClient for CDN download (URL is pre-signed, no auth needed)
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var bytesRead = 0L;
            var lastReport = Stopwatch.StartNew();

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;

                    // Throttled: one report per 8 KB chunk used to flood the UI dispatcher.
                    if (totalBytes > 0 && lastReport.ElapsedMilliseconds >= 100)
                    {
                        progress?.Report((double)bytesRead / totalBytes);
                        lastReport.Restart();
                    }
                }
            }

            File.Move(partPath, filePath, overwrite: true);
            partPath = null;

            progress?.Report(1.0);
            Log.Info($"Download complete: {filePath} ({bytesRead} bytes)");
            return filePath;
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Download cancelled for file {publishedFileId}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to download version for file {publishedFileId}", ex);
            return null;
        }
        finally
        {
            if (partPath is not null)
            {
                try { File.Delete(partPath); } catch { /* best effort */ }
            }
        }
    }

    public bool IsVersionDownloaded(uint appId, string modName, long timestamp)
    {
        var versionFolder = AppPaths.WorkshopVersionFolder(appId, SanitizeModName(modName), timestamp);
        return Directory.Exists(versionFolder) &&
               Directory.GetFiles(versionFolder, "*.zip").Length > 0;
    }

    public void OpenVersionFolder(uint appId, string modName, long timestamp)
    {
        var versionFolder = AppPaths.WorkshopVersionFolder(appId, SanitizeModName(modName), timestamp);

        if (!Directory.Exists(versionFolder))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = versionFolder,
            UseShellExecute = true
        });
    }

    public static string SanitizeModName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
            .Replace(' ', '_');

        if (sanitized.Length > 50)
            sanitized = sanitized[..50];

        return sanitized;
    }
}
