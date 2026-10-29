using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

namespace SteamWorkshopManager.Services.Workshop;

/// <summary>
/// Last known "My mods" list per game and account, shown instantly while the
/// platform is queried. The platform's answer always replaces it.
/// </summary>
public sealed class PublishedItemsCache(ICacheStore cache)
{
    private static readonly Logger Log = LogService.GetLogger<PublishedItemsCache>();

    // Per account: another login on the same machine has other mods.
    private static CacheKey Key(WorkshopSession session, ulong accountId)
    {
        var (platform, scope) = CacheKey.SessionScope(session);
        return new CacheKey(platform, scope, $"items-{accountId}.json");
    }

    public async Task<List<WorkshopItem>?> LoadAsync(WorkshopSession session, ulong accountId)
    {
        if (accountId == 0) return null;

        try
        {
            var entry = await Task.Run(() => cache.GetAsync(Key(session, accountId), PublishedItemsJsonContext.Default.ListWorkshopItemDto));
            return entry?.Value.Select(WorkshopItemMapper.ToModel).ToList();
        }
        catch (Exception ex)
        {
            Log.Debug($"Cached items unreadable for {session}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Snapshots <paramref name="items"/> on the calling thread, writes in the background.</summary>
    public Task SaveAsync(WorkshopSession session, ulong accountId, IEnumerable<WorkshopItem> items)
    {
        if (accountId == 0) return Task.CompletedTask;

        var key = Key(session, accountId);
        var snapshot = items.Select(WorkshopItemMapper.ToDto).ToList();
        return Task.Run(async () =>
        {
            try
            {
                await cache.SetAsync(key, snapshot, PublishedItemsJsonContext.Default.ListWorkshopItemDto);
            }
            catch (Exception ex)
            {
                Log.Debug($"Items cache write failed for {session}: {ex.Message}");
            }
        });
    }
}

[JsonSerializable(typeof(List<WorkshopItemDto>))]
internal partial class PublishedItemsJsonContext : JsonSerializerContext;
