using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using Steamworks;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>
/// Shell-side facade over the worker's app-dependency RPC. SteamUGC work runs
/// in the worker; the public Steam Store HTTP lookup for app display names
/// stays on the shell side since it doesn't need the SteamAPI.
/// </summary>
public sealed class AppDependencyService(SessionHost host)
{
    private static readonly Logger Log = LogService.GetLogger<AppDependencyService>();
    private static readonly HttpClient HttpClient = new();
    private static readonly Dictionary<uint, string?> AppNameCache = new();

    public async Task<List<AppDependencyInfo>> GetAppDependenciesAsync(PublishedFileId_t modId)
    {
        if (host.Worker is null) return [];
        var dtos = await host.Worker.GetAppDependenciesAsync(modId.m_PublishedFileId);
        return dtos.Select(d => new AppDependencyInfo { AppId = d.AppId, Name = d.Name }).ToList();
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
        if (AppNameCache.TryGetValue(appId, out var cached)) return cached;

        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
            using var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) { AppNameCache[appId] = null; return null; }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(appId.ToString(), out var appData) &&
                appData.TryGetProperty("success", out var success) && success.GetBoolean() &&
                appData.TryGetProperty("data", out var data) &&
                data.TryGetProperty("name", out var name))
            {
                var appName = name.GetString();
                AppNameCache[appId] = appName;
                return appName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to resolve app name for {appId}: {ex.Message}");
        }

        AppNameCache[appId] = null;
        return null;
    }
}
