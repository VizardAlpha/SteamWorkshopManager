using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Steam;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;
using Steamworks;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

namespace SteamWorkshopManager.Services.Steam.Worker.Host;

/// <summary>Workshop writes: create, update, per-language text and delete, with upload progress.</summary>
internal sealed class SteamUgcPublisher(SteamService steam, uint appId)
{
    private static readonly Logger Log = LogService.GetLogger<SteamUgcPublisher>();

    private readonly uint _appId = appId;

    // Uploads only time out when Steam reports no progress for this long.
    private static readonly TimeSpan UploadStallTimeout = TimeSpan.FromMinutes(5);

    public async Task<CreateItemOutcome> CreateItemAsync(string title, string description,
        string contentFolderPath, string? previewImagePath, VisibilityType visibility,
        List<string> tags, string? changelog, IProgress<UploadProgress>? progress = null,
        string? branchMin = null, string? branchMax = null,
        IReadOnlyList<PreviewOp>? previewOps = null)
    {
        if (!steam.IsInitialized)
        {
            Log.Warning("CreateItemAsync called but Steam is not initialized");
            EnsureProgressDismissed(progress, "");
            return CreateItemOutcome.Failed();
        }

        Log.Info($"Creating new Workshop item: '{title}'");
        Log.Debug($"Content folder: {contentFolderPath}, Preview: {previewImagePath ?? "none"}, Visibility: {visibility}");
        Log.Debug($"Tags: {string.Join(", ", tags)}");

        var expectedTotal = ComputeExpectedTotalBytes(contentFolderPath, previewImagePath, previewOps);
        ReportProgress(progress, "CreatingItem", 0, expectedTotal, 0);

        // Set once Steam allocates the item, cleared on success; anything left is deleted.
        PublishedFileId_t? orphanId = null;
        var succeeded = false;

        try
        {
        
            // Create item
        var created = await SteamCall.AwaitAsync<CreateItemResult_t>(
            SteamUGC.CreateItem(new AppId_t(_appId), EWorkshopFileType.k_EWorkshopFileTypeCommunity),
            SteamCall.DefaultTimeout, "Item creation failed");
        if (created is not { } createResult)
        {
            Log.Error("Create item request timed out");
            return CreateItemOutcome.Failed(EResult.k_EResultTimeout);
        }

        if (createResult.m_eResult != EResult.k_EResultOK)
        {
            Log.Error($"Failed to create item: {SteamErrorMapper.GetTechnicalDescription(createResult.m_eResult)}");
            Log.Error($"User message: {SteamErrorMapper.GetCreateItemErrorMessage(createResult.m_eResult)}");
            return CreateItemOutcome.Failed(createResult.m_eResult);
        }

        var fileId = createResult.m_nPublishedFileId;
        orphanId = fileId;
        Log.Info($"Item created successfully with FileId: {fileId}");

        ReportProgress(progress, "ConfiguringItem", 0, expectedTotal, 5);

        // Update with content
        var updateHandle = SteamUGC.StartItemUpdate(
            new AppId_t(_appId),
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

        ReportProgress(progress, "Uploading", 0, expectedTotal, 10);

        var submit = SteamCall.Start<SubmitItemUpdateResult_t>(
            SteamUGC.SubmitItemUpdate(updateHandle, changelog ?? "Initial version"), "Submission failed");

        if (!await WaitForUploadAsync(submit, updateHandle, progress, expectedTotal))
        {
            Log.Error("Submit item update stalled: no progress from Steam");
            return CreateItemOutcome.Failed(EResult.k_EResultTimeout);
        }

        var submitResult = await submit;
        if (submitResult.m_eResult == EResult.k_EResultOK)
        {
            orphanId = null;
            succeeded = true;
            Log.Info($"Item '{title}' published successfully");
            ReportProgress(progress, "Done", expectedTotal, expectedTotal, 100);
            return CreateItemOutcome.Created(fileId, NeedsLegalAgreement(
                createResult.m_bUserNeedsToAcceptWorkshopLegalAgreement
                || submitResult.m_bUserNeedsToAcceptWorkshopLegalAgreement));
        }

        Log.Error($"Failed to submit item: {SteamErrorMapper.GetTechnicalDescription(submitResult.m_eResult)}");
        Log.Error($"User message: {SteamErrorMapper.GetErrorMessage(submitResult.m_eResult)}");
        return CreateItemOutcome.Failed(submitResult.m_eResult);
        }
        finally
        {
            if (orphanId is { } orphan)
                await DeleteOrphanAsync(orphan);
            if (!succeeded) EnsureProgressDismissed(progress, "OperationFailed");
        }
    }

    /// <summary>Removes the empty item left behind by a failed create so a retry doesn't pile up duplicates.</summary>
    private static bool NeedsLegalAgreement(bool flag)
    {
        if (flag) Log.Warning("Steam keeps the item hidden until the Workshop legal agreement is accepted");
        return flag;
    }

    private async Task DeleteOrphanAsync(PublishedFileId_t fileId)
    {
        Log.Warning($"Create failed after Steam allocated FileId {fileId}, deleting the empty item");
        try
        {
            if (!await DeleteItemAsync(fileId))
                Log.Warning($"Could not delete orphan item {fileId}, it stays on the Workshop");
        }
        catch (Exception ex)
        {
            Log.Warning($"Could not delete orphan item {fileId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports progress until the submit completes. Returns false only when Steam
    /// reports no status or byte change for <see cref="UploadStallTimeout"/>.
    /// </summary>
    private static async Task<bool> WaitForUploadAsync(Task submitTask, UGCUpdateHandle_t updateHandle,
        IProgress<UploadProgress>? progress, ulong expectedTotal)
    {
        var lastSignal = (EItemUpdateStatus.k_EItemUpdateStatusInvalid, 0UL);
        var stallDeadline = DateTime.UtcNow + UploadStallTimeout;

        while (!submitTask.IsCompleted)
        {
            if (DateTime.UtcNow >= stallDeadline) return false;

            var signal = PollAndReportProgress(updateHandle, progress, expectedTotal);
            if (signal != lastSignal)
            {
                lastSignal = signal;
                stallDeadline = DateTime.UtcNow + UploadStallTimeout;
            }
            await Task.Delay(100);
        }
        return true;
    }

    public async Task<UpdateItemOutcome> UpdateItemAsync(PublishedFileId_t fileId, string? title,
        string? description, string? contentFolderPath, string? previewImagePath,
        VisibilityType? visibility, List<string>? tags, string? changelog,
        IProgress<UploadProgress>? progress = null, string? branchMin = null, string? branchMax = null,
        IReadOnlyList<PreviewOp>? previewOps = null)
    {
        if (!steam.IsInitialized)
        {
            Log.Warning("UpdateItemAsync called but Steam is not initialized");
            EnsureProgressDismissed(progress, "");
            return false;
        }

        Log.Info($"Updating Workshop item: FileId={fileId}, Title='{title ?? "(unchanged)"}'");
        Log.Debug($"Content folder: {contentFolderPath ?? "(unchanged)"}, Preview: {previewImagePath ?? "(unchanged)"}");
        if (tags != null) Log.Debug($"Tags: {string.Join(", ", tags)}");

        var expectedTotal = ComputeExpectedTotalBytes(contentFolderPath, previewImagePath, previewOps);
        ReportProgress(progress, "PreparingUpdate", 0, expectedTotal, 0);
        var succeeded = false;

        try
        {
            var updateHandle = SteamUGC.StartItemUpdate(
            new AppId_t(_appId),
            fileId
        );

        // Null means unchanged; an empty description is a real edit (the user cleared it).
        if (!string.IsNullOrEmpty(title) || description is not null)
            SteamUGC.SetItemUpdateLanguage(updateHandle, SteamLanguages.BaseCode);

        if (!string.IsNullOrEmpty(title))
            SteamUGC.SetItemTitle(updateHandle, title);

        if (description is not null)
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

        ReportProgress(progress, "Uploading", 0, expectedTotal, 5);

        var submit = SteamCall.Start<SubmitItemUpdateResult_t>(
            SteamUGC.SubmitItemUpdate(updateHandle, changelog ?? ""), "Update failed");

        if (!await WaitForUploadAsync(submit, updateHandle, progress, expectedTotal))
        {
            Log.Error("Update item stalled: no progress from Steam");
            return false;
        }

        var result = await submit;
        if (result.m_eResult == EResult.k_EResultOK)
        {
            succeeded = true;
            Log.Info($"Item {fileId} updated successfully");
            ReportProgress(progress, "Done", expectedTotal, expectedTotal, 100);
            return new UpdateItemOutcome(true,
                NeedsLegalAgreement(result.m_bUserNeedsToAcceptWorkshopLegalAgreement));
        }

        Log.Error($"Failed to update item: {SteamErrorMapper.GetTechnicalDescription(result.m_eResult)}");
        Log.Error($"User message: {SteamErrorMapper.GetErrorMessage(result.m_eResult)}");
        return false;
        }
        finally
        {
            if (!succeeded) EnsureProgressDismissed(progress, "OperationFailed");
        }
    }

    public async Task<bool> UpdateItemTextAsync(PublishedFileId_t fileId, string language, ItemText text)
    {
        if (!steam.IsInitialized)
        {
            Log.Warning("UpdateItemTextAsync called but Steam is not initialized");
            return false;
        }

        // Steam stores an empty title as a blank translation, it does not delete it.
        if (string.IsNullOrWhiteSpace(text.Title))
        {
            Log.Error($"Refusing to send an empty title for {language}");
            return false;
        }

        Log.Info($"Updating item text: FileId={fileId}, Language={language}");

        var updateHandle = SteamUGC.StartItemUpdate(new AppId_t(_appId), fileId);
        var languageSet = SteamUGC.SetItemUpdateLanguage(updateHandle, language);
        var titleSet = SteamUGC.SetItemTitle(updateHandle, text.Title);
        var descriptionSet = SteamUGC.SetItemDescription(updateHandle, text.Description);
        Log.Debug($"SetItemUpdateLanguage={languageSet}, SetItemTitle={titleSet}, SetItemDescription={descriptionSet}");

        if (!languageSet)
        {
            Log.Error($"Steam rejected update language '{language}'");
            return false;
        }

        var submit = SteamCall.Start<SubmitItemUpdateResult_t>(
            SteamUGC.SubmitItemUpdate(updateHandle, ""), "Item text update failed");

        if (!await WaitForUploadAsync(submit, updateHandle, null, 0))
        {
            Log.Error($"Item text update stalled for {fileId} ({language})");
            return false;
        }

        var result = await submit;
        if (result.m_eResult == EResult.k_EResultOK)
        {
            Log.Info($"Item {fileId} text updated for {language}");
            return true;
        }

        Log.Error($"Failed to update item text ({language}): {SteamErrorMapper.GetTechnicalDescription(result.m_eResult)}");
        return false;
    }

    public async Task<bool> DeleteItemAsync(PublishedFileId_t fileId)
    {
        if (!steam.IsInitialized)
        {
            Log.Warning("DeleteItemAsync called but Steam is not initialized");
            return false;
        }

        Log.Info($"Deleting Workshop item: FileId={fileId}");

        var deleted = await SteamCall.AwaitAsync<DeleteItemResult_t>(
            SteamUGC.DeleteItem(fileId), SteamCall.DefaultTimeout, "Deletion failed");
        if (deleted is not { } result)
        {
            Log.Error("Delete item request timed out");
            return false;
        }

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
            catch { /* ignore filesystem errors - surface 0 */ }
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
    /// only during <c>k_EItemUpdateStatusUploadingContent</c> - every other
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

    /// <summary>Final progress report so the UI banner clears on every failed exit
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
    /// Returns the raw (status, bytes) pair so the caller can detect stalls.
    /// </summary>
    private static (EItemUpdateStatus Status, ulong BytesProcessed) PollAndReportProgress(
        UGCUpdateHandle_t updateHandle, IProgress<UploadProgress>? progress, ulong expectedTotal)
    {
        var status = SteamUGC.GetItemUpdateProgress(updateHandle, out var bytesProcessed, out var bytesTotal);
        var signal = (status, bytesProcessed);
        if (progress is null) return signal;

        // Localization keys: the worker has no Avalonia resources, the shell translates.
        var statusText = status switch
        {
            EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => "Preparing",
            EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => "PreparingContent",
            EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => "UploadingContent",
            EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => "UploadingImage",
            EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => "Finalizing",
            _ => "Uploading"
        };

        // During the actual content upload, Steam reports bytes against the
        // content size - close enough to expectedTotal that we can use the
        // raw values directly and watch them flow.
        if (status == EItemUpdateStatus.k_EItemUpdateStatusUploadingContent && bytesTotal > 0)
        {
            progress.Report(new UploadProgress(statusText, bytesProcessed, bytesTotal));
            return signal;
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
        return signal;
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

        Log.Debug($"Applying {ops.Count} preview op(s). The final list reflects the Add order below");

        var removes = new List<uint>();
        foreach (var op in ops)
        {
            if (op is PreviewOp.Remove r) removes.Add(r.Index);
        }
        removes.Sort((a, b) => b.CompareTo(a));
        foreach (var idx in removes)
        {
            var ok = SteamUGC.RemoveItemPreview(handle, idx);
            Log.Debug($"  RemoveItemPreview(index={idx}) = {ok}");
        }

        var addPosition = 0;
        foreach (var op in ops)
        {
            switch (op)
            {
                case PreviewOp.AddImage img:
                    var imgOk = SteamUGC.AddItemPreviewFile(handle, img.FilePath,
                        EItemPreviewType.k_EItemPreviewType_Image);
                    Log.Debug($"  [pos {addPosition++}] AddItemPreviewFile(Image, '{img.FilePath}') = {imgOk}");
                    break;
                case PreviewOp.AddVideo vid:
                    var vidOk = SteamUGC.AddItemPreviewVideo(handle, vid.YouTubeId);
                    Log.Debug($"  [pos {addPosition++}] AddItemPreviewVideo('{vid.YouTubeId}') = {vidOk}");
                    break;
            }
        }
    }

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

    // Workshop and app dependencies.

    public async Task<bool> AddDependencyAsync(ulong parentId, ulong childId)
    {
        if (!steam.IsInitialized) return false;

        var parent = new PublishedFileId_t(parentId);
        var child = new PublishedFileId_t(childId);

        var result = await SteamCall.AwaitAsync<AddUGCDependencyResult_t>(
            SteamUGC.AddDependency(parent, child), SteamCall.DefaultTimeout, "AddDependency call failed");
        return result?.m_eResult == EResult.k_EResultOK;
    }

    public async Task<bool> RemoveDependencyAsync(ulong parentId, ulong childId)
    {
        if (!steam.IsInitialized) return false;

        var parent = new PublishedFileId_t(parentId);
        var child = new PublishedFileId_t(childId);

        var result = await SteamCall.AwaitAsync<RemoveUGCDependencyResult_t>(
            SteamUGC.RemoveDependency(parent, child), SteamCall.DefaultTimeout, "RemoveDependency call failed");
        return result?.m_eResult == EResult.k_EResultOK;
    }

    public async Task<bool> AddAppDependencyAsync(ulong modId, uint appId)
    {
        if (!steam.IsInitialized) return false;

        var result = await SteamCall.AwaitAsync<AddAppDependencyResult_t>(
            SteamUGC.AddAppDependency(new PublishedFileId_t(modId), new AppId_t(appId)), SteamCall.DefaultTimeout,
            "AddAppDependency call failed");
        return result?.m_eResult == EResult.k_EResultOK;
    }

    public async Task<bool> RemoveAppDependencyAsync(ulong modId, uint appId)
    {
        if (!steam.IsInitialized) return false;

        var result = await SteamCall.AwaitAsync<RemoveAppDependencyResult_t>(
            SteamUGC.RemoveAppDependency(new PublishedFileId_t(modId), new AppId_t(appId)), SteamCall.DefaultTimeout,
            "RemoveAppDependency call failed");
        return result?.m_eResult == EResult.k_EResultOK;
    }
}
