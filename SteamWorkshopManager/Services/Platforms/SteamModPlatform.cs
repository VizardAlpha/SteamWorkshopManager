using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Steam;
using Steamworks;

namespace SteamWorkshopManager.Services.Platforms;

/// <summary>Steam Workshop through the worker.</summary>
public sealed class SteamModPlatform(ISteamService steam) : IModPlatform
{
    public WorkshopPlatform Platform => WorkshopPlatform.Steam;

    public bool IsReady => steam.IsInitialized;

    public string NotReadyMessageKey => "SteamNotInitialized";

    public ulong AccountId => steam.CurrentUserId?.m_SteamID ?? 0;

    public Task<List<WorkshopItem>> GetMyModsAsync() => steam.GetPublishedItemsAsync();

    public Task<WorkshopItem?> GetModAsync(ulong id) => steam.GetPublishedItemAsync(new PublishedFileId_t(id));

    public Task<bool> SetVisibilityAsync(ulong id, VisibilityType visibility) =>
        steam.UpdateItemAsync(new PublishedFileId_t(id),
            title: null, description: null,
            contentFolderPath: null, previewImagePath: null,
            visibility: visibility,
            tags: null, changelog: null);

    public Task<bool> DeleteModAsync(ulong id) => steam.DeleteItemAsync(new PublishedFileId_t(id));
}
