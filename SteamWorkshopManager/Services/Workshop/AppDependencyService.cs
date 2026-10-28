using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using Steamworks;

namespace SteamWorkshopManager.Services.Workshop;

/// <summary>
/// Shell-side facade over the worker's app-dependency RPC. SteamUGC work runs
/// in the worker; the public Steam Store HTTP lookup for app display names
/// stays on the shell side since it doesn't need the SteamAPI.
/// </summary>
public sealed class AppDependencyService(SessionHost host, ICacheStore cache)
{
    private static readonly Logger Log = LogService.GetLogger<AppDependencyService>();
    private static readonly HttpClient HttpClient = SteamHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(10));
    private static readonly CachePolicy NamePolicy = new(TimeSpan.FromDays(30));

    public async Task<List<AppDependencyInfo>> GetAppDependenciesAsync(PublishedFileId_t modId)
    {
        if (host.Worker is null) return [];
        var dtos = await host.Worker.GetAppDependenciesAsync(modId.m_PublishedFileId);

        // The worker only knows the AppIds; names come from the Store, looked up in parallel.
        var names = await Task.WhenAll(dtos.Select(d => d.Name is null ? ResolveAppNameAsync(d.AppId) : Task.FromResult<string?>(d.Name)));
        return dtos.Select((d, i) => new AppDependencyInfo { AppId = d.AppId, Name = names[i] }).ToList();
    }

    public async Task<bool> AddAppDependencyAsync(PublishedFileId_t modId, AppId_t appId)
    {
        if (host.Worker is null) return false;
        Log.Info($"Adding app dependency: mod={modId}, app={appId}");
        return await host.Worker.AddAppDependencyAsync(modId.m_PublishedFileId, appId.m_AppId);
    }

    public async Task<bool> RemoveAppDependencyAsync(PublishedFileId_t modId, AppId_t appId)
    {
        if (host.Worker is null) return false;
        Log.Info($"Removing app dependency: mod={modId}, app={appId}");
        return await host.Worker.RemoveAppDependencyAsync(modId.m_PublishedFileId, appId.m_AppId);
    }

    public async Task<string?> ResolveAppNameAsync(uint appId)
    {
        var entry = await cache.GetOrFetchAsync(
            CacheKey.SteamApp(appId, "name.json"), AppNameJsonContext.Default.AppNameEntry,
            () => FetchAppNameAsync(appId), NamePolicy);
        return entry?.Name;
    }

    private static async Task<AppNameEntry?> FetchAppNameAsync(uint appId)
    {
        try
        {
            var url = SteamUrls.AppDetails(appId);
            using var response = await HttpClient.GetAsync(url);
            // Transient failures (rate limit, outage) aren't cached so a later render retries.
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            // A missing name is an answer too (delisted app): cached as null.
            var appName = SteamAppDetailsParser.TryGetData(doc.RootElement, appId, out var data) &&
                          data.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
            return new AppNameEntry(appName);
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to resolve app name for {appId}: {ex.Message}");
            return null;
        }
    }
}

internal sealed record AppNameEntry(string? Name);

[JsonSerializable(typeof(AppNameEntry))]
internal partial class AppNameJsonContext : JsonSerializerContext;
