using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;
using Steamworks;

namespace SteamWorkshopManager.Services;

public class AppDependencyService
{
    private static readonly Logger Log = LogService.GetLogger<AppDependencyService>();
    private static readonly HttpClient HttpClient = new();
    private static readonly Dictionary<uint, string?> AppNameCache = new();

    public async Task<List<AppDependencyInfo>> GetAppDependenciesAsync(PublishedFileId_t modId)
    {
        Log.Info($"Getting app dependencies for mod {modId}");

        var tcs = new TaskCompletionSource<GetAppDependenciesResult_t>();
        var callResult = CallResult<GetAppDependenciesResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("GetAppDependencies call failed"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.GetAppDependencies(modId);
        callResult.Set(handle);

        if (!await PollCallbackAsync(tcs.Task))
        {
            Log.Error("GetAppDependencies request timed out");
            return [];
        }

        var result = await tcs.Task;
        if (result.m_eResult != EResult.k_EResultOK)
        {
            Log.Error($"GetAppDependencies failed: {result.m_eResult}");
            return [];
        }

        var dependencies = new List<AppDependencyInfo>();
        for (uint i = 0; i < result.m_nNumAppDependencies; i++)
        {
            var appId = result.m_rgAppIDs[i];
            var name = await ResolveAppNameAsync(appId.m_AppId);
            dependencies.Add(new AppDependencyInfo
            {
                AppId = appId.m_AppId,
                Name = name
            });
        }

        Log.Info($"Found {dependencies.Count} app dependencies");
        return dependencies;
    }

    public async Task<bool> AddAppDependencyAsync(PublishedFileId_t modId, AppId_t appId)
    {
        Log.Info($"Adding app dependency: mod={modId}, app={appId}");

        var tcs = new TaskCompletionSource<AddAppDependencyResult_t>();
        var callResult = CallResult<AddAppDependencyResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("AddAppDependency call failed"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.AddAppDependency(modId, appId);
        callResult.Set(handle);

        if (!await PollCallbackAsync(tcs.Task))
        {
            Log.Error("AddAppDependency request timed out");
            return false;
        }

        var result = await tcs.Task;
        if (result.m_eResult == EResult.k_EResultOK)
        {
            Log.Info($"App dependency added successfully: {appId}");
            return true;
        }

        Log.Error($"Failed to add app dependency: {result.m_eResult}");
        return false;
    }

    public async Task<bool> RemoveAppDependencyAsync(PublishedFileId_t modId, AppId_t appId)
    {
        Log.Info($"Removing app dependency: mod={modId}, app={appId}");

        var tcs = new TaskCompletionSource<RemoveAppDependencyResult_t>();
        var callResult = CallResult<RemoveAppDependencyResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("RemoveAppDependency call failed"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.RemoveAppDependency(modId, appId);
        callResult.Set(handle);

        if (!await PollCallbackAsync(tcs.Task))
        {
            Log.Error("RemoveAppDependency request timed out");
            return false;
        }

        var result = await tcs.Task;
        if (result.m_eResult == EResult.k_EResultOK)
        {
            Log.Info($"App dependency removed successfully: {appId}");
            return true;
        }

        Log.Error($"Failed to remove app dependency: {result.m_eResult}");
        return false;
    }

    public async Task<string?> ResolveAppNameAsync(uint appId)
    {
        if (AppNameCache.TryGetValue(appId, out var cached))
            return cached;

        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
            var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                AppNameCache[appId] = null;
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty(appId.ToString(), out var appData) &&
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

    private static async Task<bool> PollCallbackAsync(Task task, int timeoutSeconds = 30)
    {
        var timeout = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(100);
        }
        return task.IsCompleted;
    }
}
