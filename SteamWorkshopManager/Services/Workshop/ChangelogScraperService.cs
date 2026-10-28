using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Services.Workshop;

[JsonSerializable(typeof(ChangeLogEntry))]
[JsonSerializable(typeof(List<ChangeLogEntry>))]
internal partial class ChangelogJsonContext : JsonSerializerContext;

/// <summary>
/// Version history scraped from the item's changelog page. Cached per item revision (a new
/// version changes it), 7 days at most; an empty answer is never cached.
/// </summary>
public class ChangelogScraperService(SessionHost host, ICacheStore cache, ISessionContext context)
{
    private static readonly Logger Log = LogService.GetLogger<ChangelogScraperService>();
    private static readonly CachePolicy Policy = new(TimeSpan.FromDays(7), ServeStaleOnError: false);

    private static readonly Regex ChangeLogRegex = new(
        @"changeLogs\[\d+\]\s*=\s*(\{""timestamp"".*?""accountid"":\d+\});",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public async Task<List<ChangeLogEntry>> GetChangeLogsAsync(ulong publishedFileId, string revision)
    {
        // Only a signed-in page carries the manifest ids that downloads need: kept apart from the anonymous one.
        var name = SteamAuthService.IsAuthenticated ? "changelog-signed-in.json" : "changelog.json";
        var entries = await cache.GetOrFetchAsync(
            CacheKey.SteamItem(context.AppId, publishedFileId, name),
            ChangelogJsonContext.Default.ListChangeLogEntry,
            async () => await FetchChangeLogsAsync(publishedFileId) is { Count: > 0 } fetched ? fetched : null,
            Policy, version: revision);
        return entries ?? [];
    }

    private async Task<List<ChangeLogEntry>> FetchChangeLogsAsync(ulong publishedFileId)
    {
        var url = $"https://steamcommunity.com/sharedfiles/filedetails/changelog/{publishedFileId}";
        Log.Debug($"Fetching changelogs from {url}");

        try
        {
            var entries = await FetchAndParseAsync(url);
            if (entries.Count > 0)
            {
                Log.Debug($"Parsed {entries.Count} changelog entries for file {publishedFileId}");
                return entries.OrderByDescending(e => e.Timestamp).ToList();
            }

            // No manifest entries, but we *were* authenticated when we tried -
            // means Steam silently invalidated the cookie (post-download session
            // consumption, server-side TTL shorter than the JWT exp, etc.).
            // Force-mint a new access token from the refresh token and retry once.
            if (SteamAuthService.IsAuthenticated && SteamAuthService.HasRefreshToken)
            {
                Log.Warning("Authenticated scrape returned no manifest entries - refreshing access token and retrying");
                if (await SteamAuthService.TryRefreshAccessTokenAsync(forceRefresh: true))
                {
                    var retry = await FetchAndParseAsync(url);
                    if (retry.Count > 0)
                    {
                        Log.Debug($"Parsed {retry.Count} changelog entries for file {publishedFileId} after refresh");
                        return retry.OrderByDescending(e => e.Timestamp).ToList();
                    }
                }
            }

            return [];
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch changelogs for file {publishedFileId}", ex);
            return [];
        }
    }

    private async Task<List<ChangeLogEntry>> FetchAndParseAsync(string url)
    {
        string? html;
        if (SteamAuthService.IsAuthenticated)
        {
            Log.Debug("Using authenticated HttpClient for changelog fetch");
            var httpClient = SteamAuthService.GetAuthenticatedHttpClient();
            html = await httpClient.GetStringAsync(url);
        }
        else
        {
            // Anonymous path goes through the worker so SteamHTTP (with the
            // session's cookie jar) can issue the request - Steamworks is only
            // initialised in the worker process.
            Log.Debug("Routing unauthenticated changelog fetch through worker");
            html = host.Worker is null ? null : await host.Worker.FetchSteamWebAsync(url);
        }

        if (html == null)
        {
            Log.Warning("Returned null for changelog page");
            return [];
        }

        return Parse(html);
    }

    /// <summary>Extracts the <c>changeLogs[n] = {...}</c> entries embedded in the changelog page.</summary>
    internal static List<ChangeLogEntry> Parse(string html)
    {
        var matches = ChangeLogRegex.Matches(html);
        Log.Debug($"Regex found {matches.Count} changelog entries");

        var entries = new List<ChangeLogEntry>();
        foreach (Match match in matches)
        {
            try
            {
                var json = match.Groups[1].Value.Replace("\\/", "/");
                var entry = JsonSerializer.Deserialize(json, ChangelogJsonContext.Default.ChangeLogEntry);
                if (entry != null)
                    entries.Add(entry);
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to deserialize changelog JSON: {ex.Message}");
            }
        }

        return entries;
    }
}