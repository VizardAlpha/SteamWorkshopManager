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
/// Last known "My mods" list per game and Steam account, shown instantly while
/// Steam is queried. Steam's answer always replaces it.
/// </summary>
public sealed class PublishedItemsCache(ICacheStore cache)
{
    private static readonly Logger Log = LogService.GetLogger<PublishedItemsCache>();

    // Per account: another Steam login on the same machine has other mods.
    private static CacheKey Key(uint appId, ulong steamId) => CacheKey.SteamApp(appId, $"items-{steamId}.json");

    public async Task<List<WorkshopItem>?> LoadAsync(uint appId, ulong steamId)
    {
        if (appId == 0 || steamId == 0) return null;

        try
        {
            var entry = await Task.Run(() => cache.GetAsync(Key(appId, steamId), PublishedItemsJsonContext.Default.ListWorkshopItemDto));
            return entry?.Value.Select(WorkshopItemMapper.ToModel).ToList();
        }
        catch (Exception ex)
        {
            Log.Debug($"Cached items unreadable for AppId {appId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Snapshots <paramref name="items"/> on the calling thread, writes in the background.</summary>
    public Task SaveAsync(uint appId, ulong steamId, IEnumerable<WorkshopItem> items)
    {
        if (appId == 0 || steamId == 0) return Task.CompletedTask;

        var snapshot = items.Select(WorkshopItemMapper.ToDto).ToList();
        return Task.Run(async () =>
        {
            try
            {
                await cache.SetAsync(Key(appId, steamId), snapshot, PublishedItemsJsonContext.Default.ListWorkshopItemDto);
            }
            catch (Exception ex)
            {
                Log.Debug($"Items cache write failed for AppId {appId}: {ex.Message}");
            }
        });
    }
}

[JsonSerializable(typeof(List<WorkshopItemDto>))]
internal partial class PublishedItemsJsonContext : JsonSerializerContext;
