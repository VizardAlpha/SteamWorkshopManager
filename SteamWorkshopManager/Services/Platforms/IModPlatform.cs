using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services.Platforms;

/// <summary>
/// What the shared screens (home, "My mods") need from a platform. Editors and
/// creation stay platform-specific.
/// </summary>
public interface IModPlatform
{
    WorkshopPlatform Platform { get; }

    /// <summary>False while the platform can't be queried (Steam not running, signed out).</summary>
    bool IsReady { get; }

    /// <summary>Localization key explaining why <see cref="IsReady"/> is false.</summary>
    string NotReadyMessageKey { get; }

    /// <summary>Account the mods belong to, 0 when unknown. Keys the cached list.</summary>
    ulong AccountId { get; }

    /// <summary>Mods of the signed-in account for the active game.</summary>
    Task<List<WorkshopItem>> GetMyModsAsync();

    /// <summary>One mod by id, null when the platform can't resolve it (yet).</summary>
    Task<WorkshopItem?> GetModAsync(ulong id);

    Task<bool> SetVisibilityAsync(ulong id, VisibilityType visibility);

    Task<bool> DeleteModAsync(ulong id);
}
