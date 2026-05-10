using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using Steamworks;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using static SteamWorkshopManager.Services.Core.LocalizationService;

namespace SteamWorkshopManager.Services.Steam;

public class SteamService : ISteamService
{
    private static readonly Logger Log = LogService.GetLogger<SteamService>();
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public CSteamID? CurrentUserId => _isInitialized ? SteamUser.GetSteamID() : null;

    public SteamInitResult Initialize()
    {
        if (_isInitialized) return SteamInitResult.Success;

        try
        {
            Log.Info($"Initializing Steam for AppId: {AppConfig.AppId}");
            Log.Debug($"Current session: {AppConfig.CurrentSession?.GameName ?? "none"}");

            var appIdStr = AppConfig.AppId.ToString();
            var envAppId = Environment.GetEnvironmentVariable("SteamAppId");
            var envGameId = Environment.GetEnvironmentVariable("SteamGameId");

            if (envAppId != appIdStr)
            {
                Environment.SetEnvironmentVariable("SteamAppId", appIdStr);
                Log.Debug($"Set SteamAppId environment variable to {appIdStr}");
            }
            if (envGameId != appIdStr)
            {
                Environment.SetEnvironmentVariable("SteamGameId", appIdStr);
                Log.Debug($"Set SteamGameId environment variable to {appIdStr}");
            }

            Log.Debug($"Environment SteamAppId: {Environment.GetEnvironmentVariable("SteamAppId") ?? "(not set)"}");
            Log.Debug($"Environment SteamGameId: {Environment.GetEnvironmentVariable("SteamGameId") ?? "(not set)"}");

            var steamRunning = SteamAPI.IsSteamRunning();
            Log.Info($"Steam running: {steamRunning}");

            _isInitialized = SteamAPI.Init();
            Log.Info($"Steam API Init result: {_isInitialized}");

            if (_isInitialized)
            {
                var loggedOn = SteamUser.BLoggedOn();
                Log.Info($"User logged on: {loggedOn}");

                var steamAppId = SteamUtils.GetAppID();
                Log.Info($"Steam AppID from SteamUtils: {steamAppId}");

                if (steamAppId.m_AppId != AppConfig.AppId)
                {
                    Log.Warning($"AppId mismatch! Expected: {AppConfig.AppId}, Steam reports: {steamAppId.m_AppId}");
                }

                var currentBranch = GetCurrentBranchName();
                Log.Info($"Current game branch: {currentBranch}");
                var branches = GetGameBranches();
                Log.Info($"Versioning: {branches.Count} branches available (enabled={branches.Count > 0})");

                return SteamInitResult.Success;
            }

            if (steamRunning)
            {
                Log.Warning("Steam is running but API init failed - game likely not owned");
                return SteamInitResult.GameNotOwned;
            }

            Log.Warning("Steam API initialization failed - Steam is not running");
            return SteamInitResult.SteamNotRunning;
        }
        catch (Exception ex)
        {
            Log.Error($"Steam initialization exception", ex);
            return SteamInitResult.SteamNotRunning;
        }
    }

    public void Shutdown()
    {
        if (!_isInitialized) return;
        Log.Info("Shutting down Steam API");
        SteamAPI.Shutdown();
        _isInitialized = false;
    }

    public async Task<List<WorkshopItem>> GetPublishedItemsAsync()
    {
        if (!_isInitialized)
        {
            Log.Warning("GetPublishedItemsAsync called but Steam is not initialized");
            return [];
        }

        Log.Debug("Fetching published items from Steam Workshop");
        var items = new List<WorkshopItem>();
        var accountId = SteamUser.GetSteamID().GetAccountID();

        var query = SteamUGC.CreateQueryUserUGCRequest(
            accountId,
            EUserUGCList.k_EUserUGCList_Published,
            EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items,
            EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc,
            new AppId_t(AppConfig.AppId),
            new AppId_t(AppConfig.AppId),
            1
        );

        SteamUGC.SetReturnLongDescription(query, true);
        SteamUGC.SetReturnMetadata(query, true);
        SteamUGC.SetReturnAdditionalPreviews(query, true);

        var tcs = new TaskCompletionSource<SteamUGCQueryCompleted_t>();
        var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("Steam query failed"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.SendQueryUGCRequest(query);
        callResult.Set(handle);

        // Wait for callback via polling
        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(100);
        }

        if (!tcs.Task.IsCompleted)
        {
            Log.Error("Query published items request timed out");
            SteamUGC.ReleaseQueryUGCRequest(query);
            return items;
        }

        var queryResult = await tcs.Task;
        Log.Debug($"Query returned {queryResult.m_unNumResultsReturned} items (EResult: {queryResult.m_eResult})");

        var currentUserId = SteamUser.GetSteamID();
        for (uint i = 0; i < queryResult.m_unNumResultsReturned; i++)
        {
            var item = ReadWorkshopItemAt(queryResult.m_handle, i, currentUserId);
            if (item is not null) items.Add(item);
        }

        SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        Log.Info($"Successfully fetched {items.Count} published items");
        return items;
    }

    /// <summary>
    /// Fetches a single Workshop item by its <see cref="PublishedFileId_t"/>.
    /// Used after Create/Update so the shell can refresh just that row instead
    /// of re-querying the user's whole catalog. Returns null if Steam can't
    /// resolve the id (e.g. indexing latency right after publish).
    /// </summary>
    public async Task<WorkshopItem?> GetPublishedItemAsync(PublishedFileId_t fileId)
    {
        if (!_isInitialized)
        {
            Log.Warning("GetPublishedItemAsync called but Steam is not initialized");
            return null;
        }

        Log.Debug($"Fetching single Workshop item: {fileId}");

        var query = SteamUGC.CreateQueryUGCDetailsRequest([fileId], 1);
        SteamUGC.SetReturnLongDescription(query, true);
        SteamUGC.SetReturnMetadata(query, true);
        SteamUGC.SetReturnAdditionalPreviews(query, true);

        var tcs = new TaskCompletionSource<SteamUGCQueryCompleted_t>();
        var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) =>
        {
            if (failure) tcs.SetException(new Exception("Single-item query failed"));
            else tcs.SetResult(result);
        });
        callResult.Set(SteamUGC.SendQueryUGCRequest(query));

        var timeout = DateTime.UtcNow.AddSeconds(15);
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(50);
        }

        if (!tcs.Task.IsCompleted)
        {
            Log.Error($"Single-item query timed out for {fileId}");
            SteamUGC.ReleaseQueryUGCRequest(query);
            return null;
        }

        var queryResult = await tcs.Task;
        if (queryResult.m_eResult != EResult.k_EResultOK || queryResult.m_unNumResultsReturned == 0)
        {
            Log.Warning($"Single-item query returned no result for {fileId} (EResult: {queryResult.m_eResult})");
            SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
            return null;
        }

        var item = ReadWorkshopItemAt(queryResult.m_handle, 0, SteamUser.GetSteamID());
        SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        return item;
    }

    /// <summary>
    /// Builds a <see cref="WorkshopItem"/> from one row of a UGC query result.
    /// Shared by the bulk and single-item fetch paths.
    /// </summary>
    private static WorkshopItem? ReadWorkshopItemAt(UGCQueryHandle_t handle, uint index, CSteamID currentUserId)
    {
        if (!SteamUGC.GetQueryUGCResult(handle, index, out var details))
            return null;

        var tags = new List<WorkshopTag>();
        if (!string.IsNullOrEmpty(details.m_rgchTags))
        {
            foreach (var tag in details.m_rgchTags.Split(','))
                tags.Add(new WorkshopTag(tag.Trim(), true));
        }

        string? previewUrl = null;
        if (SteamUGC.GetQueryUGCPreviewURL(handle, index, out var url, 1024))
            previewUrl = url;

        SteamUGC.GetQueryUGCStatistic(handle, index,
            EItemStatistic.k_EItemStatistic_NumSubscriptions, out var subscribers);

        var ownerId = new CSteamID(details.m_ulSteamIDOwner);

        return new WorkshopItem
        {
            PublishedFileId = details.m_nPublishedFileId,
            Title = details.m_rgchTitle,
            Description = details.m_rgchDescription,
            PreviewImageUrl = previewUrl,
            Visibility = MapVisibility(details.m_eVisibility),
            Tags = tags,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeCreated).DateTime,
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeUpdated).DateTime,
            SubscriberCount = subscribers,
            FileSize = details.m_nFileSize,
            OwnerId = ownerId,
            IsOwner = ownerId == currentUserId,
            AdditionalPreviews = ReadAdditionalPreviews(handle, index),
        };
    }

    public async Task<PublishedFileId_t?> CreateItemAsync(string title, string description,
        string contentFolderPath, string? previewImagePath, VisibilityType visibility,
        List<string> tags, string? changelog, IProgress<UploadProgress>? progress = null,
        string? branchMin = null, string? branchMax = null,
        IReadOnlyList<PreviewOp>? previewOps = null)
    {
        if (!_isInitialized)
        {
            Log.Warning("CreateItemAsync called but Steam is not initialized");
            EnsureProgressDismissed(progress, "");
            return null;
        }

        Log.Info($"Creating new Workshop item: '{title}'");
        Log.Debug($"Content folder: {contentFolderPath}, Preview: {previewImagePath ?? "none"}, Visibility: {visibility}");
        Log.Debug($"Tags: {string.Join(", ", tags)}");

        var expectedTotal = ComputeExpectedTotalBytes(contentFolderPath, previewImagePath, previewOps);
        ReportProgress(progress, GetString("CreatingItem"), 0, expectedTotal, 0);

        try
        {

        // Create item
        var createTcs = new TaskCompletionSource<CreateItemResult_t>();
        var createCallResult = CallResult<CreateItemResult_t>.Create((result, failure) =>
        {
            if (failure)
                createTcs.SetException(new Exception("Item creation failed"));
            else
                createTcs.SetResult(result);
        });

        var createHandle = SteamUGC.CreateItem(
            new AppId_t(AppConfig.AppId),
            EWorkshopFileType.k_EWorkshopFileTypeCommunity
        );
        createCallResult.Set(createHandle);

        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (!createTcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(100);
        }

        if (!createTcs.Task.IsCompleted)
        {
            Log.Error("Create item request timed out");
            return null;
        }

        var createResult = await createTcs.Task;
        if (createResult.m_eResult != EResult.k_EResultOK)
        {
            Log.Error($"Failed to create item: {SteamErrorMapper.GetTechnicalDescription(createResult.m_eResult)}");
            Log.Error($"User message: {SteamErrorMapper.GetErrorMessage(createResult.m_eResult)}");
            return null;
        }

        var fileId = createResult.m_nPublishedFileId;
        Log.Info($"Item created successfully with FileId: {fileId}");

        ReportProgress(progress, GetString("ConfiguringItem"), 0, expectedTotal, 5);

        // Update with content
        var updateHandle = SteamUGC.StartItemUpdate(
            new AppId_t(AppConfig.AppId),
            fileId
        );

        SteamUGC.SetItemTitle(updateHandle, title);
        SteamUGC.SetItemDescription(updateHandle, description);
        SteamUGC.SetItemContent(updateHandle, contentFolderPath);
        SteamUGC.SetItemVisibility(updateHandle, MapToSteamVisibility(visibility));

        if (!string.IsNullOrEmpty(previewImagePath))
            SteamUGC.SetItemPreview(updateHandle, previewImagePath);

        if (tags.Count > 0)
            SteamUGC.SetItemTags(updateHandle, tags);

        if (branchMin != null || branchMax != null)
        {
            var versionResult = SteamUGC.SetRequiredGameVersions(updateHandle, branchMin ?? "", branchMax ?? "");
            Log.Debug($"SetRequiredGameVersions(min='{branchMin ?? ""}', max='{branchMax ?? ""}'): {versionResult}");
        }

        ApplyPreviewOps(updateHandle, previewOps);

        var submitTcs = new TaskCompletionSource<SubmitItemUpdateResult_t>();
        var submitCallResult = CallResult<SubmitItemUpdateResult_t>.Create((result, failure) =>
        {
            if (failure)
                submitTcs.SetException(new Exception("Submission failed"));
            else
                submitTcs.SetResult(result);
        });

        ReportProgress(progress, GetString("Uploading"), 0, expectedTotal, 10);

        var submitHandle = SteamUGC.SubmitItemUpdate(updateHandle, changelog ?? "Initial version");
        submitCallResult.Set(submitHandle);

        timeout = DateTime.UtcNow.AddSeconds(300); // 5 minutes for large uploads
        while (!submitTcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            PollAndReportProgress(updateHandle, progress, expectedTotal);
            await Task.Delay(100);
        }

        if (!submitTcs.Task.IsCompleted)
        {
            Log.Error("Submit item update request timed out");
            return null;
        }

        var submitResult = await submitTcs.Task;
        if (submitResult.m_eResult == EResult.k_EResultOK)
        {
            Log.Info($"Item '{title}' published successfully");
            ReportProgress(progress, GetString("Done"), expectedTotal, expectedTotal, 100);
            return fileId;
        }

        Log.Error($"Failed to submit item: {SteamErrorMapper.GetTechnicalDescription(submitResult.m_eResult)}");
        Log.Error($"User message: {SteamErrorMapper.GetErrorMessage(submitResult.m_eResult)}");
        return null;
        }
        finally
        {
            EnsureProgressDismissed(progress, GetString("OperationFailed"));
        }
    }

    public async Task<bool> UpdateItemAsync(PublishedFileId_t fileId, string? title,
        string? description, string? contentFolderPath, string? previewImagePath,
        VisibilityType? visibility, List<string>? tags, string? changelog,
        IProgress<UploadProgress>? progress = null, string? branchMin = null, string? branchMax = null,
        IReadOnlyList<PreviewOp>? previewOps = null)
    {
        if (!_isInitialized)
        {
            Log.Warning("UpdateItemAsync called but Steam is not initialized");
            EnsureProgressDismissed(progress, "");
            return false;
        }

        Log.Info($"Updating Workshop item: FileId={fileId}, Title='{title ?? "(unchanged)"}'");
        Log.Debug($"Content folder: {contentFolderPath ?? "(unchanged)"}, Preview: {previewImagePath ?? "(unchanged)"}");
        if (tags != null) Log.Debug($"Tags: {string.Join(", ", tags)}");

        var expectedTotal = ComputeExpectedTotalBytes(contentFolderPath, previewImagePath, previewOps);
        ReportProgress(progress, GetString("PreparingUpdate"), 0, expectedTotal, 0);

        try
        {

        var updateHandle = SteamUGC.StartItemUpdate(
            new AppId_t(AppConfig.AppId),
            fileId
        );

        if (!string.IsNullOrEmpty(title))
            SteamUGC.SetItemTitle(updateHandle, title);

        if (!string.IsNullOrEmpty(description))
            SteamUGC.SetItemDescription(updateHandle, description);

        if (!string.IsNullOrEmpty(contentFolderPath))
            SteamUGC.SetItemContent(updateHandle, contentFolderPath);

        if (!string.IsNullOrEmpty(previewImagePath))
            SteamUGC.SetItemPreview(updateHandle, previewImagePath);

        if (visibility.HasValue)
            SteamUGC.SetItemVisibility(updateHandle, MapToSteamVisibility(visibility.Value));

        // Always update tags if list is provided (empty list = remove all tags)
        if (tags != null)
            SteamUGC.SetItemTags(updateHandle, tags);

        if (branchMin != null || branchMax != null)
        {
            var versionResult = SteamUGC.SetRequiredGameVersions(updateHandle, branchMin ?? "", branchMax ?? "");
            Log.Debug($"SetRequiredGameVersions(min='{branchMin ?? ""}', max='{branchMax ?? ""}'): {versionResult}");
        }

        ApplyPreviewOps(updateHandle, previewOps);

        var tcs = new TaskCompletionSource<SubmitItemUpdateResult_t>();
        var callResult = CallResult<SubmitItemUpdateResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("Update failed"));
            else
                tcs.SetResult(result);
        });

        ReportProgress(progress, GetString("Uploading"), 0, expectedTotal, 5);

        var handle = SteamUGC.SubmitItemUpdate(updateHandle, changelog ?? "");
        callResult.Set(handle);

        var timeout = DateTime.UtcNow.AddSeconds(300); // 5 minutes for large uploads
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            PollAndReportProgress(updateHandle, progress, expectedTotal);
            await Task.Delay(100);
        }

        if (!tcs.Task.IsCompleted)
        {
            Log.Error("Update item request timed out");
            return false;
        }

        var result = await tcs.Task;
        if (result.m_eResult == EResult.k_EResultOK)
        {
            Log.Info($"Item {fileId} updated successfully");
            ReportProgress(progress, GetString("Done"), expectedTotal, expectedTotal, 100);
            return true;
        }

        Log.Error($"Failed to update item: {SteamErrorMapper.GetTechnicalDescription(result.m_eResult)}");
        Log.Error($"User message: {SteamErrorMapper.GetErrorMessage(result.m_eResult)}");
        return false;
        }
        finally
        {
            EnsureProgressDismissed(progress, GetString("OperationFailed"));
        }
    }

    public async Task<bool> DeleteItemAsync(PublishedFileId_t fileId)
    {
        if (!_isInitialized)
        {
            Log.Warning("DeleteItemAsync called but Steam is not initialized");
            return false;
        }

        Log.Info($"Deleting Workshop item: FileId={fileId}");

        var tcs = new TaskCompletionSource<DeleteItemResult_t>();
        var callResult = CallResult<DeleteItemResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("Deletion failed"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.DeleteItem(fileId);
        callResult.Set(handle);

        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(100);
        }

        if (!tcs.Task.IsCompleted)
        {
            Log.Error("Delete item request timed out");
            return false;
        }

        var result = await tcs.Task;
        if (result.m_eResult == EResult.k_EResultOK)
        {
            Log.Info($"Item {fileId} deleted successfully");
            return true;
        }

        Log.Error($"Failed to delete item: {SteamErrorMapper.GetTechnicalDescription(result.m_eResult)}");
        Log.Error($"User message: {SteamErrorMapper.GetErrorMessage(result.m_eResult)}");
        return false;
    }
    
    /// <summary>
    /// Sum the sizes of every artifact actually being shipped to Steam: the
    /// content folder, the main preview image, and any local image files in
    /// the preview-ops list. Returns 0 for metadata-only updates (tags,
    /// title, visibility…) so the UI doesn't display a phantom "0 / 0 MB".
    /// </summary>
    private static ulong ComputeExpectedTotalBytes(
        string? contentFolderPath, string? previewImagePath, IReadOnlyList<PreviewOp>? previewOps)
    {
        ulong total = 0;
        if (!string.IsNullOrEmpty(contentFolderPath) && Directory.Exists(contentFolderPath))
        {
            try
            {
                foreach (var f in new DirectoryInfo(contentFolderPath).GetFiles("*", SearchOption.AllDirectories))
                    total += (ulong)f.Length;
            }
            catch { /* ignore filesystem errors — surface 0 */ }
        }
        if (!string.IsNullOrEmpty(previewImagePath) && File.Exists(previewImagePath))
        {
            try { total += (ulong)new FileInfo(previewImagePath).Length; } catch { /* ignore */ }
        }
        if (previewOps != null)
        {
            foreach (var op in previewOps)
            {
                if (op is PreviewOp.AddImage img && File.Exists(img.FilePath))
                {
                    try { total += (ulong)new FileInfo(img.FilePath).Length; } catch { /* ignore */ }
                }
            }
        }
        return total;
    }

    /// <summary>
    /// Single emit point for upload progress. Steam streams real byte counts
    /// only during <c>k_EItemUpdateStatusUploadingContent</c> — every other
    /// phase (preparing, configuring, committing) is reported with
    /// <paramref name="bytesProcessed"/> = 0. In those phases we hide the byte
    /// counter and let <paramref name="percentHint"/> drive the bar; otherwise
    /// <c>BytesTotal &gt; 0</c> + <c>BytesProcessed == 0</c> would pin the
    /// progress bar at 0% until the content upload phase actually starts.
    /// </summary>
    private static void ReportProgress(IProgress<UploadProgress>? progress,
        string status, ulong bytesProcessed, ulong expectedTotal, double percentHint)
    {
        if (progress is null) return;
        if (bytesProcessed > 0 && expectedTotal > 0)
            progress.Report(new UploadProgress(status, bytesProcessed, expectedTotal, percentHint));
        else
            progress.Report(new UploadProgress(status, 0, 0, percentHint));
    }

    /// <summary>Final progress report so the UI banner clears on every exit
    /// path, including timeouts and Steam-side failures.</summary>
    private static void EnsureProgressDismissed(IProgress<UploadProgress>? progress, string status)
    {
        progress?.Report(new UploadProgress(status, 0, 0, 100));
    }

    /// <summary>
    /// Translates Steam's per-phase <c>GetItemUpdateProgress</c> readout into
    /// a single, stable progress signal. We report bytes against the
    /// pre-computed <paramref name="expectedTotal"/> rather than Steam's
    /// per-phase total so the user doesn't see the displayed total jump
    /// between phases (preview file = 1 MB, content = 100 MB, etc.).
    /// </summary>
    private static void PollAndReportProgress(UGCUpdateHandle_t updateHandle,
        IProgress<UploadProgress>? progress, ulong expectedTotal)
    {
        if (progress is null) return;

        var status = SteamUGC.GetItemUpdateProgress(updateHandle, out var bytesProcessed, out var bytesTotal);
        var statusText = status switch
        {
            EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => GetString("Preparing"),
            EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => GetString("PreparingContent"),
            EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => GetString("UploadingContent"),
            EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => GetString("UploadingImage"),
            EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => GetString("Finalizing"),
            _ => GetString("Uploading")
        };

        // During the actual content upload, Steam reports bytes against the
        // content size — close enough to expectedTotal that we can use the
        // raw values directly and watch them flow.
        if (status == EItemUpdateStatus.k_EItemUpdateStatusUploadingContent && bytesTotal > 0)
        {
            progress.Report(new UploadProgress(statusText, bytesProcessed, bytesTotal));
            return;
        }

        // Other phases: keep the total stable and drive the bar by hint
        // alone, rather than briefly flashing a different scale (e.g. the
        // preview file phase reports a sub-MB total that would replace a
        // multi-GB content total on screen).
        var hint = status switch
        {
            EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => 5.0,
            EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => 10.0,
            EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => 15.0,
            EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => 95.0,
            _ => 0.0,
        };
        ReportProgress(progress, statusText, 0, expectedTotal, hint);
    }

    /// <summary>
    /// Apply staged preview mutations (add image / add video / remove by
    /// index) to an open update handle. Removals run first, sorted by
    /// descending index so the in-flight reindexing Steam performs after
    /// each <c>RemoveItemPreview</c> doesn't shift indices we still need to
    /// reference.
    /// </summary>
    private static void ApplyPreviewOps(UGCUpdateHandle_t handle, IReadOnlyList<PreviewOp>? ops)
    {
        if (ops == null || ops.Count == 0) return;

        Log.Info($"Applying {ops.Count} preview op(s) — final list will reflect Add order below");

        var removes = new List<uint>();
        foreach (var op in ops)
        {
            if (op is PreviewOp.Remove r) removes.Add(r.Index);
        }
        removes.Sort((a, b) => b.CompareTo(a));
        foreach (var idx in removes)
        {
            var ok = SteamUGC.RemoveItemPreview(handle, idx);
            Log.Info($"  RemoveItemPreview(index={idx}) = {ok}");
        }

        var addPosition = 0;
        foreach (var op in ops)
        {
            switch (op)
            {
                case PreviewOp.AddImage img:
                    var imgOk = SteamUGC.AddItemPreviewFile(handle, img.FilePath,
                        EItemPreviewType.k_EItemPreviewType_Image);
                    Log.Info($"  [pos {addPosition++}] AddItemPreviewFile(Image, '{img.FilePath}') = {imgOk}");
                    break;
                case PreviewOp.AddVideo vid:
                    var vidOk = SteamUGC.AddItemPreviewVideo(handle, vid.YouTubeId);
                    Log.Info($"  [pos {addPosition++}] AddItemPreviewVideo('{vid.YouTubeId}') = {vidOk}");
                    break;
            }
        }
    }

    private static List<WorkshopPreview> ReadAdditionalPreviews(UGCQueryHandle_t handle, uint itemIndex)
    {
        var result = new List<WorkshopPreview>();
        var count = SteamUGC.GetQueryUGCNumAdditionalPreviews(handle, itemIndex);
        Log.Debug($"Item {itemIndex}: {count} additional preview(s)");

        for (uint p = 0; p < count; p++)
        {
            if (!SteamUGC.GetQueryUGCAdditionalPreview(handle, itemIndex, p,
                    out var urlOrVideoId, 1024,
                    out var originalFilename, 256,
                    out var previewType))
            {
                Log.Debug($"GetQueryUGCAdditionalPreview failed at item={itemIndex} preview={p}");
                continue;
            }
            Log.Debug($"  preview {p}: type={previewType}, url='{urlOrVideoId}', file='{originalFilename}'");

            result.Add(new WorkshopPreview
            {
                Source = WorkshopPreviewSource.Existing,
                PreviewType = previewType,
                OriginalIndex = p,
                RemoteUrl = urlOrVideoId,
                OriginalFilename = originalFilename,
            });
        }

        // Sort defensively in case Steam ever returns previews out of order;
        // every downstream consumer (editor list, reorder detection, save-time
        // ops) assumes the list is OriginalIndex-ascending.
        result.Sort((a, b) => a.OriginalIndex.CompareTo(b.OriginalIndex));
        return result;
    }

    private static VisibilityType MapVisibility(ERemoteStoragePublishedFileVisibility visibility) =>
        visibility switch
        {
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic =>
                VisibilityType.Public,
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly =>
                VisibilityType.FriendsOnly,
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate =>
                VisibilityType.Private,
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted =>
                VisibilityType.Unlisted,
            _ => VisibilityType.Private
        };

    private static ERemoteStoragePublishedFileVisibility MapToSteamVisibility(VisibilityType visibility) =>
        visibility switch
        {
            VisibilityType.Public =>
                ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
            VisibilityType.FriendsOnly =>
                ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
            VisibilityType.Private =>
                ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
            VisibilityType.Unlisted =>
                ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
            _ => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate
        };

    public List<GameBranch> GetGameBranches()
    {
        var branches = new List<GameBranch>();
        if (!_isInitialized)
        {
            Log.Warning("GetGameBranches called but Steam is not initialized");
            return branches;
        }

        try
        {
            var totalBetas = SteamApps.GetNumBetas(out var available, out var privateBetas);
            Log.Debug($"Game branches: total={totalBetas}, available={available}, private={privateBetas}");

            for (var i = 0; i < totalBetas; i++)
            {
                if (SteamApps.GetBetaInfo(i, out var flags, out var buildId,
                        out var betaName, 128, out var description, 256))
                {
                    branches.Add(new GameBranch
                    {
                        Name = betaName,
                        Description = description,
                        BuildId = buildId,
                        Flags = flags
                    });
                }
                else
                {
                    Log.Warning($"Failed to get beta info for index {i}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to get game branches", ex);
        }

        return branches;
    }

    public string GetCurrentBranchName()
    {
        if (!_isInitialized) return "public";

        try
        {
            if (SteamApps.GetCurrentBetaName(out var betaName, 128) && !string.IsNullOrEmpty(betaName))
            {
                Log.Debug($"Current beta branch: {betaName}");
                return betaName;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to get current beta name", ex);
        }

        return "public";
    }
}
