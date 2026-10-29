using System.Collections.Generic;
using System.Linq;
using SteamWorkshopManager.Core.Sessions;

namespace SteamWorkshopManager.Services.Platforms;

/// <summary>Picks the <see cref="IModPlatform"/> of the active session.</summary>
public sealed class ModPlatforms(IEnumerable<IModPlatform> platforms, ISessionContext context)
{
    private readonly IReadOnlyList<IModPlatform> _platforms = platforms.ToList();

    /// <summary>Null when no session is active or its platform isn't available in this build.</summary>
    public IModPlatform? Current =>
        context.Platform is { } platform ? _platforms.FirstOrDefault(p => p.Platform == platform) : null;
}
