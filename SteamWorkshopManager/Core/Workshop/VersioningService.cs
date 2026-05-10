using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using Steamworks;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>
/// Shell-side facade around the game's branch/version surface. Branch
/// metadata flows through <see cref="ISteamService"/> (already RPC-proxied);
/// per-item supported-game-version lookups require a SteamUGC query and
/// route through the worker's <c>GetSupportedGameVersionsAsync</c> RPC.
/// </summary>
public sealed class VersioningService(ISteamService steamService, SessionHost host)
{
    private static readonly Logger Log = LogService.GetLogger<VersioningService>();

    public bool IsVersioningEnabled() => steamService.GetGameBranches().Count > 0;

    public List<GameBranch> GetAvailableBranches() =>
        steamService.GetGameBranches().Where(b => !b.IsPrivate).ToList();

    public string GetCurrentBranch() => steamService.GetCurrentBranchName();

    public async Task<List<ModVersionInfo>> GetModVersionsAsync(PublishedFileId_t fileId)
    {
        if (host.Worker is null) return [];

        var dtos = await host.Worker.GetSupportedGameVersionsAsync(fileId.m_PublishedFileId);
        Log.Info($"Item {fileId}: {dtos.Count} supported game versions");
        return dtos.Select(d => new ModVersionInfo
        {
            VersionIndex = d.VersionIndex,
            BranchMin = d.BranchMin,
            BranchMax = d.BranchMax,
        }).ToList();
    }
}
