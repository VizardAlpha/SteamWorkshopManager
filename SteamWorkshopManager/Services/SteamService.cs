using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using Steamworks;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;
using static SteamWorkshopManager.Services.LocalizationService;

namespace SteamWorkshopManager.Services;

public class SteamService : ISteamService
{
    private static readonly Logger Log = LogService.GetLogger<SteamService>();
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public CSteamID? CurrentUserId => _isInitialized ? SteamUser.GetSteamID() : null;

    public bool Initialize()
    {
        if (_isInitialized) return true;

        try
        {
            // Log the AppId we'll be using
            Log.Info($"Initializing Steam for AppId: {AppConfig.AppId}");
            Log.Debug($"Current session: {AppConfig.CurrentSession?.GameName ?? "none"}");

            // Ensure environment variables are set for Steam API
            // This is required when launching the .exe directly (not after session switch)
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

                // Log the AppId that Steam is using
                var steamAppId = SteamUtils.GetAppID();
                Log.Info($"Steam AppID from SteamUtils: {steamAppId}");

                if (steamAppId.m_AppId != AppConfig.AppId)
                {
                    Log.Warning($"AppId mismatch! Expected: {AppConfig.AppId}, Steam reports: {steamAppId.m_AppId}");
                }
            }
            else
            {
                Log.Warning("Steam API initialization failed - Steam may not be running");
            }

            return _isInitialized;
        }
        catch (Exception ex)
        {
            Log.Error($"Steam initialization exception", ex);
            return false;
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

        for (uint i = 0; i < queryResult.m_unNumResultsReturned; i++)
        {
            if (SteamUGC.GetQueryUGCResult(queryResult.m_handle, i, out var details))
            {
                var tags = new List<WorkshopTag>();
                if (!string.IsNullOrEmpty(details.m_rgchTags))
                {
                    foreach (var tag in details.m_rgchTags.Split(','))
                    {
                        tags.Add(new WorkshopTag(tag.Trim(), true));
                    }
                }

                // Get preview image URL
                string? previewUrl = null;
                if (SteamUGC.GetQueryUGCPreviewURL(queryResult.m_handle, i, out var url, 1024))
                {
                    previewUrl = url;
                    Log.Debug($"Item '{details.m_rgchTitle}' preview URL: {url}");
                }
                else
                {
                    Log.Debug($"Item '{details.m_rgchTitle}' - No preview URL found");
                }

                var ownerId = new CSteamID(details.m_ulSteamIDOwner);
                var currentUserId = SteamUser.GetSteamID();

                items.Add(new WorkshopItem
                {
                    PublishedFileId = details.m_nPublishedFileId,
                    Title = details.m_rgchTitle,
                    Description = details.m_rgchDescription,
                    PreviewImageUrl = previewUrl,
                    Visibility = MapVisibility(details.m_eVisibility),
                    Tags = tags,
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeCreated).DateTime,
                    UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeUpdated).DateTime,
                    OwnerId = ownerId,
                    IsOwner = ownerId == currentUserId
                });
            }
        }

        SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        Log.Info($"Successfully fetched {items.Count} published items");
        return items;
    }

    public async Task<PublishedFileId_t?> CreateItemAsync(string title, string description,
        string contentFolderPath, string? previewImagePath, VisibilityType visibility,
        List<string> tags, string? changelog, IProgress<UploadProgress>? progress = null)
    {
        if (!_isInitialized)
        {
            Log.Warning("CreateItemAsync called but Steam is not initialized");
            return null;
        }

        Log.Info($"Creating new Workshop item: '{title}'");
        Log.Debug($"Content folder: {contentFolderPath}, Preview: {previewImagePath ?? "none"}, Visibility: {visibility}");
        Log.Debug($"Tags: {string.Join(", ", tags)}");

        progress?.Report(new UploadProgress(GetString("CreatingItem"), 0, 100));

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

        progress?.Report(new UploadProgress(GetString("ConfiguringItem"), 10, 100));

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

        var submitTcs = new TaskCompletionSource<SubmitItemUpdateResult_t>();
        var submitCallResult = CallResult<SubmitItemUpdateResult_t>.Create((result, failure) =>
        {
            if (failure)
                submitTcs.SetException(new Exception("Submission failed"));
            else
                submitTcs.SetResult(result);
        });

        progress?.Report(new UploadProgress(GetString("Uploading"), 20, 100));

        var submitHandle = SteamUGC.SubmitItemUpdate(updateHandle, changelog ?? "Initial version");
        submitCallResult.Set(submitHandle);

        timeout = DateTime.UtcNow.AddSeconds(300); // 5 minutes for large uploads
        while (!submitTcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();

            // Get upload progress
            var status = SteamUGC.GetItemUpdateProgress(updateHandle, out var bytesProcessed, out var bytesTotal);
            if (bytesTotal > 0)
            {
                var statusText = status switch
                {
                    EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => GetString("Preparing"),
                    EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => GetString("PreparingContent"),
                    EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => GetString("UploadingContent"),
                    EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => GetString("UploadingImage"),
                    EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => GetString("Finalizing"),
                    _ => GetString("Uploading")
                };
                progress?.Report(new UploadProgress(statusText, bytesProcessed, bytesTotal));
            }

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
            progress?.Report(new UploadProgress(GetString("Done"), 100, 100));
            return fileId;
        }

        Log.Error($"Failed to submit item: {SteamErrorMapper.GetTechnicalDescription(submitResult.m_eResult)}");
        Log.Error($"User message: {SteamErrorMapper.GetErrorMessage(submitResult.m_eResult)}");
        return null;
    }

    public async Task<bool> UpdateItemAsync(PublishedFileId_t fileId, string? title,
        string? description, string? contentFolderPath, string? previewImagePath,
        VisibilityType? visibility, List<string>? tags, string? changelog,
        IProgress<UploadProgress>? progress = null)
    {
        if (!_isInitialized)
        {
            Log.Warning("UpdateItemAsync called but Steam is not initialized");
            return false;
        }

        Log.Info($"Updating Workshop item: FileId={fileId}, Title='{title ?? "(unchanged)"}'");
        Log.Debug($"Content folder: {contentFolderPath ?? "(unchanged)"}, Preview: {previewImagePath ?? "(unchanged)"}");
        if (tags != null) Log.Debug($"Tags: {string.Join(", ", tags)}");

        progress?.Report(new UploadProgress(GetString("PreparingUpdate"), 0, 100));

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

        var tcs = new TaskCompletionSource<SubmitItemUpdateResult_t>();
        var callResult = CallResult<SubmitItemUpdateResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("Update failed"));
            else
                tcs.SetResult(result);
        });

        progress?.Report(new UploadProgress(GetString("Uploading"), 10, 100));

        var handle = SteamUGC.SubmitItemUpdate(updateHandle, changelog ?? "");
        callResult.Set(handle);

        var timeout = DateTime.UtcNow.AddSeconds(300); // 5 minutes for large uploads
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();

            // Get upload progress
            var status = SteamUGC.GetItemUpdateProgress(updateHandle, out var bytesProcessed, out var bytesTotal);
            if (bytesTotal > 0)
            {
                var statusText = status switch
                {
                    EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => GetString("Preparing"),
                    EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => GetString("PreparingContent"),
                    EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => GetString("UploadingContent"),
                    EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => GetString("UploadingImage"),
                    EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => GetString("Finalizing"),
                    _ => GetString("Uploading")
                };
                progress?.Report(new UploadProgress(statusText, bytesProcessed, bytesTotal));
            }

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
            progress?.Report(new UploadProgress(GetString("Done"), 100, 100));
            return true;
        }

        Log.Error($"Failed to update item: {SteamErrorMapper.GetTechnicalDescription(result.m_eResult)}");
        Log.Error($"User message: {SteamErrorMapper.GetErrorMessage(result.m_eResult)}");
        return false;
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
}
