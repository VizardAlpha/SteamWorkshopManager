using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;
using Steamworks;

namespace SteamWorkshopManager.Services;

public class DependencyService
{
    private static readonly Logger Log = LogService.GetLogger<DependencyService>();

    public async Task<List<DependencyInfo>> GetDependenciesAsync(PublishedFileId_t parentId)
    {
        var dependencies = new List<DependencyInfo>();

        // Query the parent item with children
        var childIds = await GetChildIdsAsync(parentId);
        if (childIds.Count == 0)
            return dependencies;

        // Batch query child details (max 50 per query)
        for (var i = 0; i < childIds.Count; i += 50)
        {
            var batch = childIds.GetRange(i, Math.Min(50, childIds.Count - i));
            var details = await GetBatchDetailsAsync(batch);
            dependencies.AddRange(details);
        }

        return dependencies;
    }

    public async Task<bool> AddDependencyAsync(PublishedFileId_t parentId, PublishedFileId_t childId)
    {
        Log.Info($"Adding dependency: parent={parentId}, child={childId}");

        var tcs = new TaskCompletionSource<AddUGCDependencyResult_t>();
        var callResult = CallResult<AddUGCDependencyResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("AddDependency call failed"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.AddDependency(parentId, childId);
        callResult.Set(handle);

        if (!await PollCallbackAsync(tcs.Task))
        {
            Log.Error("AddDependency request timed out");
            return false;
        }

        var result = await tcs.Task;
        if (result.m_eResult == EResult.k_EResultOK)
        {
            Log.Info($"Dependency added successfully: {childId}");
            return true;
        }

        Log.Error($"Failed to add dependency: {result.m_eResult}");
        return false;
    }

    public async Task<bool> RemoveDependencyAsync(PublishedFileId_t parentId, PublishedFileId_t childId)
    {
        Log.Info($"Removing dependency: parent={parentId}, child={childId}");

        var tcs = new TaskCompletionSource<RemoveUGCDependencyResult_t>();
        var callResult = CallResult<RemoveUGCDependencyResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("RemoveDependency call failed"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.RemoveDependency(parentId, childId);
        callResult.Set(handle);

        if (!await PollCallbackAsync(tcs.Task))
        {
            Log.Error("RemoveDependency request timed out");
            return false;
        }

        var result = await tcs.Task;
        if (result.m_eResult == EResult.k_EResultOK)
        {
            Log.Info($"Dependency removed successfully: {childId}");
            return true;
        }

        Log.Error($"Failed to remove dependency: {result.m_eResult}");
        return false;
    }

    public async Task<DependencyInfo?> GetModDetailsAsync(PublishedFileId_t fileId)
    {
        var ids = new List<PublishedFileId_t> { fileId };
        var results = await GetBatchDetailsAsync(ids);
        return results.Count > 0 ? results[0] : null;
    }

    private async Task<List<PublishedFileId_t>> GetChildIdsAsync(PublishedFileId_t parentId)
    {
        var childIds = new List<PublishedFileId_t>();

        var query = SteamUGC.CreateQueryUGCDetailsRequest([parentId], 1);
        SteamUGC.SetReturnChildren(query, true);

        var tcs = new TaskCompletionSource<SteamUGCQueryCompleted_t>();
        var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("Query children failed"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.SendQueryUGCRequest(query);
        callResult.Set(handle);

        if (!await PollCallbackAsync(tcs.Task))
        {
            Log.Error("GetChildren query timed out");
            SteamUGC.ReleaseQueryUGCRequest(query);
            return childIds;
        }

        var queryResult = await tcs.Task;
        if (queryResult.m_eResult != EResult.k_EResultOK || queryResult.m_unNumResultsReturned == 0)
        {
            SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
            return childIds;
        }

        // Get details to find children count
        if (SteamUGC.GetQueryUGCResult(queryResult.m_handle, 0, out var details))
        {
            var numChildren = details.m_unNumChildren;
            if (numChildren > 0)
            {
                var children = new PublishedFileId_t[numChildren];
                if (SteamUGC.GetQueryUGCChildren(queryResult.m_handle, 0, children, numChildren))
                {
                    childIds.AddRange(children);
                }
            }
        }

        SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        Log.Debug($"Found {childIds.Count} children for item {parentId}");
        return childIds;
    }

    private async Task<List<DependencyInfo>> GetBatchDetailsAsync(List<PublishedFileId_t> fileIds)
    {
        var results = new List<DependencyInfo>();
        if (fileIds.Count == 0) return results;

        var query = SteamUGC.CreateQueryUGCDetailsRequest(fileIds.ToArray(), (uint)fileIds.Count);

        var tcs = new TaskCompletionSource<SteamUGCQueryCompleted_t>();
        var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("Batch details query failed"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.SendQueryUGCRequest(query);
        callResult.Set(handle);

        if (!await PollCallbackAsync(tcs.Task))
        {
            Log.Error("Batch details query timed out");
            SteamUGC.ReleaseQueryUGCRequest(query);
            return results;
        }

        var queryResult = await tcs.Task;
        if (queryResult.m_eResult != EResult.k_EResultOK)
        {
            SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
            return results;
        }

        for (uint i = 0; i < queryResult.m_unNumResultsReturned; i++)
        {
            if (SteamUGC.GetQueryUGCResult(queryResult.m_handle, i, out var details))
            {
                var previewUrl = "";
                if (SteamUGC.GetQueryUGCPreviewURL(queryResult.m_handle, i, out var url, 1024))
                    previewUrl = url;

                results.Add(new DependencyInfo
                {
                    PublishedFileId = (ulong)details.m_nPublishedFileId,
                    Title = details.m_rgchTitle,
                    PreviewUrl = previewUrl,
                    IsValid = details.m_eResult == EResult.k_EResultOK
                });
            }
        }

        SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        return results;
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
