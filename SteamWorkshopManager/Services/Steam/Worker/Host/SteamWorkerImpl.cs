using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam.Worker.Contracts;
using SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

namespace SteamWorkshopManager.Services.Steam.Worker.Host;

/// <summary>
/// Concrete <see cref="ISteamWorker"/> running inside the worker process.
///
/// All Steamworks.NET interaction lives here: the worker owns its own
/// <see cref="SteamService"/> instance, so <c>SteamAPI.Init()</c> happens in
/// this process only and the shell never touches the global Steam state.
/// RPC calls are thin wrappers — they marshal domain models to the DTOs
/// defined under <see cref="Contracts.Dtos"/>, nothing more.
/// </summary>
internal sealed class SteamWorkerImpl : ISteamWorker
{
    private static readonly Logger Log = LogService.GetLogger<SteamWorkerImpl>();
    private static readonly HttpClient HttpClient = new();
    private static readonly Dictionary<uint, string?> AppNameCache = new();

    private readonly SteamService _steam = new();
    private CancellationTokenSource? _logSinkCts;

    public Task<string> PingAsync() => Task.FromResult("pong");

    public async Task SetLogSinkAsync(IProgress<LogEntryDto>? sink, bool debugEnabled)
    {
        LogService.Instance.SetDebugMode(debugEnabled);

        _logSinkCts?.Cancel();
        _logSinkCts?.Dispose();
        _logSinkCts = null;

        if (sink == null)
        {
            LogService.Instance.SetRemoteSink(null);
            return;
        }

        LogService.Instance.SetRemoteSink(entry => sink.Report(new LogEntryDto(
            (int)entry.Level,
            entry.Source,
            entry.Message,
            entry.Exception,
            entry.Timestamp.ToUniversalTime())));

        // StreamJsonRpc IProgress<T> only stays alive for the duration of the
        // call that received it — return now and the sink dies. Hold the call
        // open until cancellation so every subsequent worker log keeps flowing.
        _logSinkCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(Timeout.Infinite, _logSinkCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            LogService.Instance.SetRemoteSink(null);
        }
    }

    public Task SetDebugModeAsync(bool enabled)
    {
        LogService.Instance.SetDebugMode(enabled);
        return Task.CompletedTask;
    }

    public Task<SteamInitResult> InitializeAsync() =>
        Task.FromResult(_steam.Initialize());

    public Task ShutdownAsync()
    {
        _steam.Shutdown();
        return Task.CompletedTask;
    }

    public Task<bool> IsInitializedAsync() =>
        Task.FromResult(_steam.IsInitialized);

    public Task<ulong> GetCurrentUserIdAsync() =>
        Task.FromResult(_steam.CurrentUserId?.m_SteamID ?? 0UL);

    public async Task<List<WorkshopItemDto>> GetPublishedItemsAsync()
    {
        var items = await _steam.GetPublishedItemsAsync();
        return items.Select(ToDto).ToList();
    }

    public Task<bool> DeleteItemAsync(ulong publishedFileId) =>
        _steam.DeleteItemAsync(new PublishedFileId_t(publishedFileId));

    public async Task<ulong> CreateItemAsync(CreateItemRequestDto request, IProgress<UploadProgressDto>? progress)
    {
        var bridge = BridgeProgress(progress);
        var fileId = await _steam.CreateItemAsync(
            request.Title,
            request.Description,
            request.ContentFolderPath,
            request.PreviewImagePath,
            request.Visibility,
            request.Tags,
            request.Changelog,
            bridge,
            request.BranchMin,
            request.BranchMax,
            request.PreviewOps?.Select(FromDto).ToList());
        return fileId?.m_PublishedFileId ?? 0UL;
    }

    public Task<bool> UpdateItemAsync(UpdateItemRequestDto request, IProgress<UploadProgressDto>? progress)
    {
        var bridge = BridgeProgress(progress);
        return _steam.UpdateItemAsync(
            new PublishedFileId_t(request.PublishedFileId),
            request.Title,
            request.Description,
            request.ContentFolderPath,
            request.PreviewImagePath,
            request.Visibility,
            request.Tags,
            request.Changelog,
            bridge,
            request.BranchMin,
            request.BranchMax,
            request.PreviewOps?.Select(FromDto).ToList());
    }

    private static PreviewOp FromDto(PreviewOpDto dto) => dto.Kind switch
    {
        PreviewOpKind.Remove => new PreviewOp.Remove(dto.RemoveIndex ?? 0u),
        PreviewOpKind.AddImage => new PreviewOp.AddImage(dto.FilePath ?? ""),
        PreviewOpKind.AddVideo => new PreviewOp.AddVideo(dto.YouTubeId ?? ""),
        _ => throw new ArgumentOutOfRangeException(nameof(dto)),
    };

    /// <summary>
    /// Adapts the shell-bound <see cref="UploadProgressDto"/> channel back to
    /// the <see cref="UploadProgress"/> type the local SteamService reports
    /// against. Returns null when no shell-side observer is attached, so we
    /// don't pay the allocation cost of a wrapper that reports into the void.
    /// </summary>
    private static IProgress<UploadProgress>? BridgeProgress(IProgress<UploadProgressDto>? sink)
    {
        if (sink is null) return null;
        return new Progress<UploadProgress>(p =>
            sink.Report(new UploadProgressDto(p.Status, p.BytesProcessed, p.BytesTotal, p.PercentHint)));
    }

    public async Task<List<DependencyInfoDto>> GetDependenciesAsync(ulong parentId)
    {
        var childIds = await GetChildIdsAsync(new PublishedFileId_t(parentId));
        if (childIds.Count == 0) return [];

        var results = new List<DependencyInfoDto>();
        for (var i = 0; i < childIds.Count; i += 50)
        {
            var batch = childIds.GetRange(i, Math.Min(50, childIds.Count - i));
            results.AddRange(await GetBatchDetailsAsync(batch));
        }
        return results;
    }

    public async Task<DependencyInfoDto?> GetModDetailsAsync(ulong fileId)
    {
        var results = await GetBatchDetailsAsync([new PublishedFileId_t(fileId)]);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<bool> AddDependencyAsync(ulong parentId, ulong childId)
    {
        var parent = new PublishedFileId_t(parentId);
        var child = new PublishedFileId_t(childId);

        var tcs = new TaskCompletionSource<AddUGCDependencyResult_t>();
        var callResult = CallResult<AddUGCDependencyResult_t>.Create((result, failure) =>
        {
            if (failure) tcs.SetException(new Exception("AddDependency call failed"));
            else tcs.SetResult(result);
        });
        callResult.Set(SteamUGC.AddDependency(parent, child));

        if (!await PollCallbackAsync(tcs.Task)) return false;
        var result = await tcs.Task;
        return result.m_eResult == EResult.k_EResultOK;
    }

    public async Task<bool> RemoveDependencyAsync(ulong parentId, ulong childId)
    {
        var parent = new PublishedFileId_t(parentId);
        var child = new PublishedFileId_t(childId);

        var tcs = new TaskCompletionSource<RemoveUGCDependencyResult_t>();
        var callResult = CallResult<RemoveUGCDependencyResult_t>.Create((result, failure) =>
        {
            if (failure) tcs.SetException(new Exception("RemoveDependency call failed"));
            else tcs.SetResult(result);
        });
        callResult.Set(SteamUGC.RemoveDependency(parent, child));

        if (!await PollCallbackAsync(tcs.Task)) return false;
        var result = await tcs.Task;
        return result.m_eResult == EResult.k_EResultOK;
    }

    public async Task<List<AppDependencyInfoDto>> GetAppDependenciesAsync(ulong modId)
    {
        var tcs = new TaskCompletionSource<GetAppDependenciesResult_t>();
        var callResult = CallResult<GetAppDependenciesResult_t>.Create((result, failure) =>
        {
            if (failure) tcs.SetException(new Exception("GetAppDependencies call failed"));
            else tcs.SetResult(result);
        });
        callResult.Set(SteamUGC.GetAppDependencies(new PublishedFileId_t(modId)));

        if (!await PollCallbackAsync(tcs.Task)) return [];
        var result = await tcs.Task;
        if (result.m_eResult != EResult.k_EResultOK) return [];

        var deps = new List<AppDependencyInfoDto>();
        for (uint i = 0; i < result.m_nNumAppDependencies; i++)
        {
            var appId = result.m_rgAppIDs[i];
            var name = await ResolveAppNameAsync(appId.m_AppId);
            deps.Add(new AppDependencyInfoDto(appId.m_AppId, name));
        }
        return deps;
    }

    public async Task<bool> AddAppDependencyAsync(ulong modId, uint appId)
    {
        var tcs = new TaskCompletionSource<AddAppDependencyResult_t>();
        var callResult = CallResult<AddAppDependencyResult_t>.Create((result, failure) =>
        {
            if (failure) tcs.SetException(new Exception("AddAppDependency call failed"));
            else tcs.SetResult(result);
        });
        callResult.Set(SteamUGC.AddAppDependency(new PublishedFileId_t(modId), new AppId_t(appId)));

        if (!await PollCallbackAsync(tcs.Task)) return false;
        var result = await tcs.Task;
        return result.m_eResult == EResult.k_EResultOK;
    }

    public async Task<bool> RemoveAppDependencyAsync(ulong modId, uint appId)
    {
        var tcs = new TaskCompletionSource<RemoveAppDependencyResult_t>();
        var callResult = CallResult<RemoveAppDependencyResult_t>.Create((result, failure) =>
        {
            if (failure) tcs.SetException(new Exception("RemoveAppDependency call failed"));
            else tcs.SetResult(result);
        });
        callResult.Set(SteamUGC.RemoveAppDependency(new PublishedFileId_t(modId), new AppId_t(appId)));

        if (!await PollCallbackAsync(tcs.Task)) return false;
        var result = await tcs.Task;
        return result.m_eResult == EResult.k_EResultOK;
    }

    public async Task<List<ModVersionInfoDto>> GetSupportedGameVersionsAsync(ulong fileId)
    {
        var versions = new List<ModVersionInfoDto>();
        try
        {
            var query = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(fileId)], 1);

            var tcs = new TaskCompletionSource<SteamUGCQueryCompleted_t>();
            var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) =>
            {
                if (failure) tcs.SetException(new Exception("Version query failed"));
                else tcs.SetResult(result);
            });
            callResult.Set(SteamUGC.SendQueryUGCRequest(query));

            if (!await PollCallbackAsync(tcs.Task))
            {
                SteamUGC.ReleaseQueryUGCRequest(query);
                return versions;
            }

            var queryResult = await tcs.Task;
            if (queryResult.m_eResult != EResult.k_EResultOK || queryResult.m_unNumResultsReturned == 0)
            {
                SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
                return versions;
            }

            var numVersions = SteamUGC.GetNumSupportedGameVersions(queryResult.m_handle, 0);
            for (uint i = 0; i < numVersions; i++)
            {
                if (SteamUGC.GetSupportedGameVersionData(queryResult.m_handle, 0, i,
                        out var branchMin, out var branchMax, 128))
                {
                    versions.Add(new ModVersionInfoDto(i, branchMin, branchMax));
                }
            }

            SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        }
        catch (Exception ex)
        {
            Log.Error($"GetSupportedGameVersions failed: {ex.Message}", ex);
        }
        return versions;
    }

    public async Task<string?> FetchSteamWebAsync(string url)
    {
        // Routed here so the SteamHTTP cookie container & request lifecycle stay
        // inside the worker process — only here is SteamAPI initialised.
        await SteamWebClient.InitializeAsync();
        return await SteamWebClient.GetStringAsync(url);
    }

    // ---- Steam UGC query helpers (worker-local) ----

    private static async Task<List<PublishedFileId_t>> GetChildIdsAsync(PublishedFileId_t parentId)
    {
        var childIds = new List<PublishedFileId_t>();
        var query = SteamUGC.CreateQueryUGCDetailsRequest([parentId], 1);
        SteamUGC.SetReturnChildren(query, true);

        var tcs = new TaskCompletionSource<SteamUGCQueryCompleted_t>();
        var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) =>
        {
            if (failure) tcs.SetException(new Exception("Query children failed"));
            else tcs.SetResult(result);
        });
        callResult.Set(SteamUGC.SendQueryUGCRequest(query));

        if (!await PollCallbackAsync(tcs.Task))
        {
            SteamUGC.ReleaseQueryUGCRequest(query);
            return childIds;
        }

        var queryResult = await tcs.Task;
        if (queryResult.m_eResult != EResult.k_EResultOK || queryResult.m_unNumResultsReturned == 0)
        {
            SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
            return childIds;
        }

        if (SteamUGC.GetQueryUGCResult(queryResult.m_handle, 0, out var details) && details.m_unNumChildren > 0)
        {
            var children = new PublishedFileId_t[details.m_unNumChildren];
            if (SteamUGC.GetQueryUGCChildren(queryResult.m_handle, 0, children, details.m_unNumChildren))
                childIds.AddRange(children);
        }

        SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        return childIds;
    }

    private static async Task<List<DependencyInfoDto>> GetBatchDetailsAsync(List<PublishedFileId_t> fileIds)
    {
        var results = new List<DependencyInfoDto>();
        if (fileIds.Count == 0) return results;

        var query = SteamUGC.CreateQueryUGCDetailsRequest(fileIds.ToArray(), (uint)fileIds.Count);

        var tcs = new TaskCompletionSource<SteamUGCQueryCompleted_t>();
        var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) =>
        {
            if (failure) tcs.SetException(new Exception("Batch details query failed"));
            else tcs.SetResult(result);
        });
        callResult.Set(SteamUGC.SendQueryUGCRequest(query));

        if (!await PollCallbackAsync(tcs.Task))
        {
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

                results.Add(new DependencyInfoDto(
                    PublishedFileId: (ulong)details.m_nPublishedFileId,
                    Title: details.m_rgchTitle,
                    PreviewUrl: previewUrl,
                    IsValid: details.m_eResult == EResult.k_EResultOK));
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

    /// <summary>
    /// Resolves a Steam App name via the public Store API. Cached in-process to
    /// spare hitting Valve for every Dependency's render.
    /// </summary>
    private static async Task<string?> ResolveAppNameAsync(uint appId)
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

    public Task<List<GameBranchDto>> GetGameBranchesAsync()
    {
        var branches = _steam.GetGameBranches()
            .Select(b => new GameBranchDto(b.Name, b.Description, b.BuildId, b.Flags))
            .ToList();
        return Task.FromResult(branches);
    }

    public Task<string> GetCurrentBranchNameAsync() =>
        Task.FromResult(_steam.GetCurrentBranchName());

    private static WorkshopItemDto ToDto(Models.WorkshopItem item) => new(
        PublishedFileId: item.PublishedFileId.m_PublishedFileId,
        Title: item.Title,
        Description: item.Description,
        PreviewImagePath: item.PreviewImagePath,
        PreviewImageUrl: item.PreviewImageUrl,
        Visibility: item.Visibility,
        Tags: item.Tags.Select(t => new WorkshopTagDto(t.Name)).ToList(),
        CreatedAt: item.CreatedAt,
        UpdatedAt: item.UpdatedAt,
        SubscriberCount: item.SubscriberCount,
        FileSize: item.FileSize,
        OwnerId: item.OwnerId.m_SteamID,
        IsOwner: item.IsOwner,
        AdditionalPreviews: item.AdditionalPreviews.Select(p => new WorkshopPreviewDto(
            (int)p.PreviewType, p.OriginalIndex, p.RemoteUrl, p.OriginalFilename)).ToList()
    );
}
