using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services;

public class WorkshopDownloadService
{
    private static readonly Logger Log = LogService.GetLogger<WorkshopDownloadService>();
    private readonly HttpClient _httpClient;

    private static readonly string WorkshopBasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager",
        "workshop"
    );

    public WorkshopDownloadService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SteamWorkshopManager/1.0");
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
                using var authClient = SteamAuthService.CreateAuthenticatedHttpClient();
                json = await authClient.GetStringAsync(url);
            }
            else
            {
                Log.Debug("Using unauthenticated SteamWebClient for download URL request");
                await SteamWebClient.InitializeAsync();
                json = await SteamWebClient.GetStringAsync(url);
            }

            if (json == null)
            {
                Log.Warning("Returned null for download URL request");
                return null;
            }

            var response = JsonSerializer.Deserialize<DownloadFileResponse>(json);

            if (response is { Success: 1, Url: not null })
            {
                Log.Info($"Got download URL for file {publishedFileId} revision {revision}");
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
        ChangeLogEntry entry, IProgress<double>? progress = null)
    {
        try
        {
            var downloadUrl = await GetDownloadUrlAsync(publishedFileId, entry.Timestamp, entry.ManifestId);
            if (downloadUrl == null)
                return null;

            var sanitizedName = SanitizeModName(modName);
            var versionFolder = Path.Combine(WorkshopBasePath, appId.ToString(), $"{sanitizedName}_{entry.Timestamp}");
            Directory.CreateDirectory(versionFolder);

            var filePath = Path.Combine(versionFolder, $"{sanitizedName}_{entry.Timestamp}.zip");

            Log.Info($"Downloading version to {filePath}");

            // Use regular HttpClient for CDN download (URL is pre-signed, no auth needed)
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var bytesRead = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int read;
            while ((read = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                bytesRead += read;

                if (totalBytes > 0)
                    progress?.Report((double)bytesRead / totalBytes);
            }

            progress?.Report(1.0);
            Log.Info($"Download complete: {filePath} ({bytesRead} bytes)");
            return filePath;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to download version for file {publishedFileId}", ex);
            return null;
        }
    }

    public bool IsVersionDownloaded(uint appId, string modName, long timestamp)
    {
        var sanitizedName = SanitizeModName(modName);
        var versionFolder = Path.Combine(WorkshopBasePath, appId.ToString(), $"{sanitizedName}_{timestamp}");
        return Directory.Exists(versionFolder) &&
               Directory.GetFiles(versionFolder, "*.zip").Length > 0;
    }

    public void OpenVersionFolder(uint appId, string modName, long timestamp)
    {
        var sanitizedName = SanitizeModName(modName);
        var versionFolder = Path.Combine(WorkshopBasePath, appId.ToString(), $"{sanitizedName}_{timestamp}");

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
