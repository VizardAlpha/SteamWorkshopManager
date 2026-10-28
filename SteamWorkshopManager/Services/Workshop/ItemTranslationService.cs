using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using Steamworks;

namespace SteamWorkshopManager.Services.Workshop;

/// <summary>
/// Shell-side facade over the worker's per-language title/description RPC. Answers are
/// cached per item revision, with a 7-day cap for edits made on the Steam website.
/// </summary>
public sealed class ItemTranslationService(SessionHost host, ICacheStore cache, ISessionContext context)
{
    private static readonly Logger Log = LogService.GetLogger<ItemTranslationService>();
    private static readonly CachePolicy Policy = new(TimeSpan.FromDays(7), ServeStaleOnError: false);

    private CacheKey Key(PublishedFileId_t fileId, string language, bool fullDescription) =>
        CacheKey.SteamItem(context.AppId, fileId.m_PublishedFileId,
            $"text-{language.ToLowerInvariant()}{(fullDescription ? "" : "-short")}.json");

    /// <summary>Null when Steam did not answer. Short mode returns a truncated description.</summary>
    public async Task<ItemText?> GetTextAsync(PublishedFileId_t fileId, string language, bool fullDescription, string revision)
    {
        if (host.Worker is null) return null;

        return await cache.GetOrFetchAsync(Key(fileId, language, fullDescription), ItemTextJsonContext.Default.ItemText,
            async () =>
            {
                if (host.Worker is not { } worker) return null;
                var dto = await worker.GetItemTextAsync(fileId.m_PublishedFileId, language, fullDescription);
                return dto is null ? null : new ItemText(dto.Title, dto.Description);
            },
            Policy, version: revision);
    }

    public async Task<bool> UpdateTextAsync(PublishedFileId_t fileId, string language, ItemText text)
    {
        if (host.Worker is null) return false;
        Log.Info($"Sending translation: item={fileId}, language={language}");
        var sent = await host.Worker.UpdateItemTextAsync(fileId.m_PublishedFileId, language, text.Title, text.Description);

        // Whatever Steam kept, the cached text is now unreliable.
        cache.Remove(Key(fileId, language, fullDescription: true));
        cache.Remove(Key(fileId, language, fullDescription: false));
        return sent;
    }
}

[JsonSerializable(typeof(ItemText))]
internal partial class ItemTextJsonContext : JsonSerializerContext;
