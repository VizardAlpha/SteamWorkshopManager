using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using Steamworks;

namespace SteamWorkshopManager.Services.Workshop;

/// <summary>
/// Shell-side facade around the game's branch/version surface. Branch
/// metadata flows through <see cref="ISteamService"/> (already RPC-proxied);
/// per-item supported-game-version lookups require a SteamUGC query and
/// route through the worker's <c>GetSupportedGameVersionsAsync</c> RPC.
///
/// Branches are cached per AppId - Steam's beta list doesn't change during a
/// session, and every editor open used to trigger 2 redundant RPCs (and
/// matching log lines). Cache invalidates automatically when the active
/// session swaps to a different AppId.
/// </summary>
public sealed class VersioningService(ISteamService steamService, SessionHost host, ISessionContext context)
{
    private static readonly Logger Log = LogService.GetLogger<VersioningService>();

    private uint _cachedAppId;
    private List<GameBranch>? _cachedBranches;
    private string? _cachedCurrentBranch;

    /// <summary>False when the worker call failed; nothing is cached then so the next open retries.</summary>
    private async Task<bool> EnsureBranchesCachedAsync()
    {
        var currentAppId = context.AppId;
        if (_cachedBranches is not null && _cachedAppId == currentAppId) return true;

        var branches = await steamService.GetGameBranchesAsync();
        if (branches is null) return false;
        var current = await steamService.GetCurrentBranchNameAsync();

        _cachedBranches = branches;
        _cachedCurrentBranch = current;
        _cachedAppId = currentAppId;
        return true;
    }

    public async Task<bool> IsVersioningEnabledAsync() =>
        await EnsureBranchesCachedAsync() && _cachedBranches!.Count > 0;

    public async Task<List<GameBranch>> GetAvailableBranchesAsync() =>
        await EnsureBranchesCachedAsync() ? _cachedBranches!.Where(b => !b.IsPrivate).ToList() : [];

    public async Task<string> GetCurrentBranchAsync() =>
        await EnsureBranchesCachedAsync() ? _cachedCurrentBranch ?? "public" : "public";

    public async Task<List<ModVersionInfo>> GetModVersionsAsync(PublishedFileId_t fileId)
    {
        if (host.Worker is null) return [];

        var dtos = await host.Worker.GetSupportedGameVersionsAsync(fileId.m_PublishedFileId);
        Log.Debug($"Item {fileId}: {dtos.Count} supported game versions");
        return dtos.Select(d => new ModVersionInfo
        {
            VersionIndex = d.VersionIndex,
            BranchMin = d.BranchMin,
            BranchMax = d.BranchMax,
        }).ToList();
    }
}
