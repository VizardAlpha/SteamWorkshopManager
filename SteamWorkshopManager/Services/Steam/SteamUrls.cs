namespace SteamWorkshopManager.Services.Steam;

/// <summary>
/// Single source of truth for the public Steam endpoints the app queries.
/// </summary>
public static class SteamUrls
{
    /// <summary>Store API returning app metadata (name, categories, ...).</summary>
    public static string AppDetails(uint appId) =>
        $"https://store.steampowered.com/api/appdetails?appids={appId}";

    /// <summary>
    /// Community Workshop landing page. Steam redirects to the game hub when the
    /// app has no Workshop, so the final URL doubles as a Workshop probe.
    /// </summary>
    public static string WorkshopPage(uint appId) =>
        $"https://steamcommunity.com/app/{appId}/workshop/";

    /// <summary>Store header image (wide, ~460x215) served by the Steam CDN.</summary>
    public static string HeaderImage(uint appId) =>
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

    /// <summary>Path segment that tells a Workshop page apart from the game hub.</summary>
    public const string WorkshopPathSegment = "/workshop";
}
