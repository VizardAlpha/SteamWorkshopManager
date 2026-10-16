using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using Steamworks;

namespace SteamWorkshopManager.Services.Workshop;

/// <summary>Shell-side facade over the worker's per-language title/description RPC.</summary>
public sealed class ItemTranslationService(SessionHost host)
{
    private static readonly Logger Log = LogService.GetLogger<ItemTranslationService>();

    /// <summary>Null when Steam did not answer. Short mode returns a truncated description.</summary>
    public async Task<ItemText?> GetTextAsync(PublishedFileId_t fileId, string language, bool fullDescription)
    {
        if (host.Worker is null) return null;
        var dto = await host.Worker.GetItemTextAsync(fileId.m_PublishedFileId, language, fullDescription);
        return dto is null ? null : new ItemText(dto.Title, dto.Description);
    }

    public async Task<bool> UpdateTextAsync(PublishedFileId_t fileId, string language, ItemText text)
    {
        if (host.Worker is null) return false;
        Log.Info($"Sending translation: item={fileId}, language={language}");
        return await host.Worker.UpdateItemTextAsync(fileId.m_PublishedFileId, language, text.Title, text.Description);
    }
}
