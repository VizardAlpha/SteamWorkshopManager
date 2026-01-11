using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using Steamworks;

namespace SteamWorkshopManager.Services;

public class SteamService : ISteamService
{
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public CSteamID? CurrentUserId => _isInitialized ? SteamUser.GetSteamID() : null;

    public bool Initialize()
    {
        if (_isInitialized) return true;

        try
        {
            Console.WriteLine($"Steam running: {SteamAPI.IsSteamRunning()}");
        
            _isInitialized = SteamAPI.Init();
        
            Console.WriteLine($"Init result: {_isInitialized}");
        
            if (_isInitialized)
            {
                Console.WriteLine($"User logged on: {SteamUser.BLoggedOn()}");
            }
        
            return _isInitialized;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
            return false;
        }
    }

    public void Shutdown()
    {
        if (!_isInitialized) return;
        SteamAPI.Shutdown();
        _isInitialized = false;
    }

    public async Task<List<WorkshopItem>> GetPublishedItemsAsync()
    {
        if (!_isInitialized) return [];

        var items = new List<WorkshopItem>();
        var accountId = SteamUser.GetSteamID().GetAccountID();

        var query = SteamUGC.CreateQueryUserUGCRequest(
            accountId,
            EUserUGCList.k_EUserUGCList_Published,
            EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items,
            EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc,
            new AppId_t(AppConstants.SongsOfSyxAppId),
            new AppId_t(AppConstants.SongsOfSyxAppId),
            1
        );

        SteamUGC.SetReturnLongDescription(query, true);
        SteamUGC.SetReturnMetadata(query, true);

        var tcs = new TaskCompletionSource<SteamUGCQueryCompleted_t>();
        var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("Erreur lors de la requête Steam"));
            else
                tcs.SetResult(result);
        });

        var handle = SteamUGC.SendQueryUGCRequest(query);
        callResult.Set(handle);

        // Attendre le callback via polling
        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(100);
        }

        if (!tcs.Task.IsCompleted)
        {
            SteamUGC.ReleaseQueryUGCRequest(query);
            return items;
        }

        var queryResult = await tcs.Task;

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

                // Récupérer l'URL de l'image preview
                string? previewUrl = null;
                if (SteamUGC.GetQueryUGCPreviewURL(queryResult.m_handle, i, out var url, 1024))
                {
                    previewUrl = url;
                    Console.WriteLine($"[DEBUG] Item '{details.m_rgchTitle}' preview URL: {url}");
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Item '{details.m_rgchTitle}' - Pas d'URL preview trouvée");
                }

                items.Add(new WorkshopItem
                {
                    PublishedFileId = details.m_nPublishedFileId,
                    Title = details.m_rgchTitle,
                    Description = details.m_rgchDescription,
                    PreviewImageUrl = previewUrl,
                    Visibility = MapVisibility(details.m_eVisibility),
                    Tags = tags,
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeCreated).DateTime,
                    UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeUpdated).DateTime
                });
            }
        }

        SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        return items;
    }

    public async Task<PublishedFileId_t?> CreateItemAsync(string title, string description,
        string contentFolderPath, string? previewImagePath, VisibilityType visibility,
        List<string> tags, string? changelog, IProgress<UploadProgress>? progress = null)
    {
        if (!_isInitialized) return null;

        progress?.Report(new UploadProgress("Création de l'item...", 0, 100));

        // Créer l'item
        var createTcs = new TaskCompletionSource<CreateItemResult_t>();
        var createCallResult = CallResult<CreateItemResult_t>.Create((result, failure) =>
        {
            if (failure)
                createTcs.SetException(new Exception("Erreur lors de la création"));
            else
                createTcs.SetResult(result);
        });

        var createHandle = SteamUGC.CreateItem(
            new AppId_t(AppConstants.SongsOfSyxAppId),
            EWorkshopFileType.k_EWorkshopFileTypeCommunity
        );
        createCallResult.Set(createHandle);

        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (!createTcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(100);
        }

        if (!createTcs.Task.IsCompleted) return null;

        var createResult = await createTcs.Task;
        if (createResult.m_eResult != EResult.k_EResultOK) return null;

        var fileId = createResult.m_nPublishedFileId;

        progress?.Report(new UploadProgress("Configuration de l'item...", 10, 100));

        // Mettre à jour avec le contenu
        var updateHandle = SteamUGC.StartItemUpdate(
            new AppId_t(AppConstants.SongsOfSyxAppId),
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
                submitTcs.SetException(new Exception("Erreur lors de la soumission"));
            else
                submitTcs.SetResult(result);
        });

        progress?.Report(new UploadProgress("Upload en cours...", 20, 100));

        var submitHandle = SteamUGC.SubmitItemUpdate(updateHandle, changelog ?? "Version initiale");
        submitCallResult.Set(submitHandle);

        timeout = DateTime.UtcNow.AddSeconds(300); // 5 minutes pour les gros uploads
        while (!submitTcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();

            // Récupérer la progression de l'upload
            var status = SteamUGC.GetItemUpdateProgress(updateHandle, out var bytesProcessed, out var bytesTotal);
            if (bytesTotal > 0)
            {
                var statusText = status switch
                {
                    EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => "Préparation...",
                    EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => "Préparation du contenu...",
                    EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => "Upload du contenu...",
                    EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => "Upload de l'image...",
                    EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => "Finalisation...",
                    _ => "Upload en cours..."
                };
                progress?.Report(new UploadProgress(statusText, bytesProcessed, bytesTotal));
            }

            await Task.Delay(100);
        }

        if (!submitTcs.Task.IsCompleted) return null;

        var submitResult = await submitTcs.Task;
        if (submitResult.m_eResult == EResult.k_EResultOK)
        {
            progress?.Report(new UploadProgress("Terminé!", 100, 100));
        }
        return submitResult.m_eResult == EResult.k_EResultOK ? fileId : null;
    }

    public async Task<bool> UpdateItemAsync(PublishedFileId_t fileId, string? title,
        string? description, string? contentFolderPath, string? previewImagePath,
        VisibilityType? visibility, List<string>? tags, string? changelog,
        IProgress<UploadProgress>? progress = null)
    {
        if (!_isInitialized) return false;

        progress?.Report(new UploadProgress("Préparation de la mise à jour...", 0, 100));

        var updateHandle = SteamUGC.StartItemUpdate(
            new AppId_t(AppConstants.SongsOfSyxAppId),
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

        // Toujours mettre à jour les tags si la liste est fournie (même vide = supprimer tous les tags)
        if (tags != null)
            SteamUGC.SetItemTags(updateHandle, tags);

        var tcs = new TaskCompletionSource<SubmitItemUpdateResult_t>();
        var callResult = CallResult<SubmitItemUpdateResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("Erreur lors de la mise à jour"));
            else
                tcs.SetResult(result);
        });

        progress?.Report(new UploadProgress("Upload en cours...", 10, 100));

        var handle = SteamUGC.SubmitItemUpdate(updateHandle, changelog ?? "");
        callResult.Set(handle);

        var timeout = DateTime.UtcNow.AddSeconds(300); // 5 minutes pour les gros uploads
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();

            // Récupérer la progression de l'upload
            var status = SteamUGC.GetItemUpdateProgress(updateHandle, out var bytesProcessed, out var bytesTotal);
            if (bytesTotal > 0)
            {
                var statusText = status switch
                {
                    EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => "Préparation...",
                    EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => "Préparation du contenu...",
                    EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => "Upload du contenu...",
                    EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => "Upload de l'image...",
                    EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => "Finalisation...",
                    _ => "Upload en cours..."
                };
                progress?.Report(new UploadProgress(statusText, bytesProcessed, bytesTotal));
            }

            await Task.Delay(100);
        }

        if (!tcs.Task.IsCompleted) return false;

        var result = await tcs.Task;
        if (result.m_eResult == EResult.k_EResultOK)
        {
            progress?.Report(new UploadProgress("Terminé!", 100, 100));
        }
        return result.m_eResult == EResult.k_EResultOK;
    }

    public async Task<bool> DeleteItemAsync(PublishedFileId_t fileId)
    {
        if (!_isInitialized) return false;

        var tcs = new TaskCompletionSource<DeleteItemResult_t>();
        var callResult = CallResult<DeleteItemResult_t>.Create((result, failure) =>
        {
            if (failure)
                tcs.SetException(new Exception("Erreur lors de la suppression"));
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

        if (!tcs.Task.IsCompleted) return false;

        var result = await tcs.Task;
        return result.m_eResult == EResult.k_EResultOK;
    }

    public List<string> GetAvailableTags() => WorkshopTags.GetAllTags();

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
