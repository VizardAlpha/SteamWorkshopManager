using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;
using Steamworks;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

namespace SteamWorkshopManager.Services.Steam.Worker.Host;

/// <summary>Workshop reads: the user's published items, one item, one item's text in a language.</summary>
internal sealed class SteamUgcQueries(SteamService steam, uint appId)
{
    private static readonly Logger Log = LogService.GetLogger<SteamUgcQueries>();

    private readonly uint _appId = appId;

    public async Task<List<WorkshopItem>> GetPublishedItemsAsync()
    {
        if (!steam.IsInitialized)
        {
            Log.Warning("GetPublishedItemsAsync called but Steam is not initialized");
            return [];
        }

        Log.Debug("Fetching published items from Steam Workshop");
        var items = new List<WorkshopItem>();
        var accountId = SteamUser.GetSteamID().GetAccountID();
        var currentUserId = SteamUser.GetSteamID();

        // Steam UGC returns up to 50 items per page (kNumUGCResultsPerPage),
        // so loop until we've drained every page.
        for (uint page = 1; ; page++)
        {
            // Throws rather than returning an empty page: a failed query must
            // never reach the UI looking like "you have published nothing".
            var queryResult = await QueryPublishedPageAsync(accountId, page);

            for (uint i = 0; i < queryResult.m_unNumResultsReturned; i++)
            {
                var item = ReadWorkshopItemAt(queryResult.m_handle, i, currentUserId);
                if (item is not null) items.Add(item);
            }

            SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);

            if (queryResult.m_unNumResultsReturned == 0 || items.Count >= queryResult.m_unTotalMatchingResults)
            {
                Log.Info($"Successfully fetched {items.Count} published items ({page} page(s))");
                return items;
            }
        }
    }

    /// <exception cref="SteamQueryException">The query could not be answered.
    /// Distinct from a query that succeeds with zero results.</exception>
    private async Task<SteamUGCQueryCompleted_t> QueryPublishedPageAsync(AccountID_t accountId, uint page)
    {
        var query = SteamUGC.CreateQueryUserUGCRequest(
            accountId,
            EUserUGCList.k_EUserUGCList_Published,
            EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items,
            EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc,
            // Any creator: items published through a separate tool (e.g. REDkit) carry the tool's AppId as creator.
            AppId_t.Invalid,
            new AppId_t(_appId),
            page
        );

        // Base text, not the viewer's Steam language: the editor writes it back as the base.
        SteamUGC.SetLanguage(query, SteamLanguages.BaseCode);
        SteamUGC.SetReturnLongDescription(query, true);
        SteamUGC.SetReturnMetadata(query, true);
        SteamUGC.SetReturnAdditionalPreviews(query, true);

        var answer = await SteamCall.QueryAsync(query, SteamCall.DefaultTimeout, "Steam did not answer the Workshop query");
        if (answer is not { } queryResult)
        {
            Log.Error($"Query published items page {page} timed out");
            throw new SteamQueryException(
                $"Steam did not answer the Workshop query within {SteamCall.DefaultTimeout.TotalSeconds:F0} seconds");
        }

        Log.Debug($"Page {page} returned {queryResult.m_unNumResultsReturned}/{queryResult.m_unTotalMatchingResults} (EResult: {queryResult.m_eResult})");

        // A user with no published items answers OK with zero results, so only
        // a non-OK result means the query itself did not go through.
        if (queryResult.m_eResult != EResult.k_EResultOK)
        {
            Log.Error($"Query published items page {page} failed: {queryResult.m_eResult}");
            SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
            throw new SteamQueryException($"Steam rejected the Workshop query ({queryResult.m_eResult})");
        }

        return queryResult;
    }

    /// <summary>
    /// Fetches a single Workshop item by its <see cref="PublishedFileId_t"/>.
    /// Used after Create/Update so the shell can refresh just that row instead
    /// of re-querying the user's whole catalog. Returns null if Steam can't
    /// resolve the id (e.g. indexing latency right after publish).
    /// </summary>
    public async Task<WorkshopItem?> GetPublishedItemAsync(PublishedFileId_t fileId)
    {
        if (!steam.IsInitialized)
        {
            Log.Warning("GetPublishedItemAsync called but Steam is not initialized");
            return null;
        }

        Log.Debug($"Fetching single Workshop item: {fileId}");

        var query = SteamUGC.CreateQueryUGCDetailsRequest([fileId], 1);
        SteamUGC.SetLanguage(query, SteamLanguages.BaseCode);
        SteamUGC.SetReturnLongDescription(query, true);
        SteamUGC.SetReturnMetadata(query, true);
        SteamUGC.SetReturnAdditionalPreviews(query, true);

        var answer = await SteamCall.QueryAsync(query, TimeSpan.FromSeconds(15), "Single-item query failed");
        if (answer is not { } queryResult)
        {
            Log.Error($"Single-item query timed out for {fileId}");
            return null;
        }

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
            Id = details.m_nPublishedFileId.m_PublishedFileId,
            Title = details.m_rgchTitle,
            Description = details.m_rgchDescription,
            PreviewImageUrl = previewUrl,
            Visibility = MapVisibility(details.m_eVisibility),
            Tags = tags,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeCreated).DateTime,
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeUpdated).DateTime,
            SubscriberCount = subscribers,
            FileSize = details.m_nFileSize,
            OwnerId = ownerId.m_SteamID,
            IsOwner = ownerId == currentUserId,
            AdditionalPreviews = ReadAdditionalPreviews(handle, index),
        };
    }

    public async Task<ItemText?> GetItemTextAsync(PublishedFileId_t fileId, string language, bool fullDescription)
    {
        if (!steam.IsInitialized)
        {
            Log.Warning("GetItemTextAsync called but Steam is not initialized");
            return null;
        }

        var query = SteamUGC.CreateQueryUGCDetailsRequest([fileId], 1);
        SteamUGC.SetLanguage(query, language);
        SteamUGC.SetReturnLongDescription(query, fullDescription);
        // The on-disk cache may hold this item's details from a query in another language.
        SteamUGC.SetAllowCachedResponse(query, 0);

        var answer = await SteamCall.QueryAsync(query, TimeSpan.FromSeconds(15), "Item text query failed");
        if (answer is not { } queryResult)
        {
            Log.Error($"Item text query timed out for {fileId} ({language})");
            return null;
        }

        ItemText? text = null;
        if (queryResult.m_eResult == EResult.k_EResultOK && queryResult.m_unNumResultsReturned > 0
            && SteamUGC.GetQueryUGCResult(queryResult.m_handle, 0, out var details))
        {
            text = new ItemText(details.m_rgchTitle, details.m_rgchDescription);
            Log.Debug($"Item text {fileId} [{language}, full={fullDescription}]: cached={queryResult.m_bCachedData}, " +
                      $"title='{text.Title}', description={text.Description.Length} chars");
        }
        else
        {
            Log.Warning($"Item text query returned no result for {fileId} ({language}, EResult: {queryResult.m_eResult})");
        }

        SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        return text;
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

    // Dependencies and supported game versions: answered straight as wire DTOs.

    public async Task<List<DependencyInfoDto>> GetDependenciesAsync(ulong parentId)
    {
        if (!steam.IsInitialized) return [];

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
        if (!steam.IsInitialized) return null;

        var results = await GetBatchDetailsAsync([new PublishedFileId_t(fileId)]);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<List<AppDependencyInfoDto>> GetAppDependenciesAsync(ulong modId)
    {
        if (!steam.IsInitialized) return [];

        var answer = await SteamCall.AwaitAsync<GetAppDependenciesResult_t>(
            SteamUGC.GetAppDependencies(new PublishedFileId_t(modId)), SteamCall.DefaultTimeout,
            "GetAppDependencies call failed");
        if (answer is not { m_eResult: EResult.k_EResultOK } result) return [];

        var deps = new List<AppDependencyInfoDto>();
        for (uint i = 0; i < result.m_nNumAppDependencies; i++)
        {
            var appId = result.m_rgAppIDs[i];
            // Names are resolved shell-side (AppDependencyService), which owns the cache.
            deps.Add(new AppDependencyInfoDto(appId.m_AppId, null));
        }
        return deps;
    }

    public async Task<List<ModVersionInfoDto>> GetSupportedGameVersionsAsync(ulong fileId)
    {
        if (!steam.IsInitialized) return [];

        var versions = new List<ModVersionInfoDto>();
        try
        {
            var query = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(fileId)], 1);

            var answer = await SteamCall.QueryAsync(query, SteamCall.DefaultTimeout, "Version query failed");
            if (answer is not { } queryResult) return versions;

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

    private static async Task<List<PublishedFileId_t>> GetChildIdsAsync(PublishedFileId_t parentId)
    {
        var childIds = new List<PublishedFileId_t>();
        var query = SteamUGC.CreateQueryUGCDetailsRequest([parentId], 1);
        SteamUGC.SetReturnChildren(query, true);

        var answer = await SteamCall.QueryAsync(query, SteamCall.DefaultTimeout, "Query children failed");
        if (answer is not { } queryResult) return childIds;

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

        var answer = await SteamCall.QueryAsync(query, SteamCall.DefaultTimeout, "Batch details query failed");
        if (answer is not { } queryResult) return results;

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
}
