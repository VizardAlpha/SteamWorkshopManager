using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services.Notifications;

public static class UpdateCheckerService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/VizardAlpha/SteamWorkshopManager/releases/latest";
    private const string AllReleasesUrl = "https://api.github.com/repos/VizardAlpha/SteamWorkshopManager/releases?per_page=10";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders =
        {
            { "User-Agent", $"SteamWorkshopManager/{AppInfo.Version}" },
            { "Accept", "application/vnd.github+json" }
        }
    };

    /// <summary>
    /// Returns an <see cref="UpdateInfo"/> when a newer release exists, else null.
    /// When <paramref name="includePrereleases"/> is true the beta channel is
    /// considered: recent releases are scanned and the highest version wins;
    /// otherwise only the latest stable release is used.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(bool includePrereleases = false)
    {
        try
        {
            var release = includePrereleases
                ? await GetHighestReleaseAsync()
                : await Http.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl);

            if (release is null) return null;
            // /releases/latest never returns a pre-release, but guard anyway.
            if (!includePrereleases && release.Prerelease) return null;

            var latestVersion = ParseVersion(release.TagName);
            var currentVersion = ParseVersion(AppInfo.Version);

            if (latestVersion is null || currentVersion is null) return null;
            if (latestVersion <= currentVersion) return null;

            return new UpdateInfo(
                AppInfo.Version,
                release.TagName,
                release.HtmlUrl,
                release.Body
            );
        }
        catch
        {
            // Non-critical - silently fail
            return null;
        }
    }

    /// <summary>
    /// Highest-versioned release across stable + pre-releases. Drafts aren't
    /// returned by the public API, so the list is safe to scan as-is.
    /// </summary>
    private static async Task<GitHubRelease?> GetHighestReleaseAsync()
    {
        var releases = await Http.GetFromJsonAsync<GitHubRelease[]>(AllReleasesUrl);
        if (releases is null) return null;

        return releases
            .Select(r => (Release: r, Version: ParseVersion(r.TagName)))
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version)
            .Select(x => x.Release)
            .FirstOrDefault();
    }

    private static Version? ParseVersion(string input)
    {
        // Strip 'v' prefix and pre-release suffix
        var cleaned = input.TrimStart('v');
        var dashIndex = cleaned.IndexOf('-');
        if (dashIndex > 0) cleaned = cleaned[..dashIndex];

        return Version.TryParse(cleaned, out var version) ? version : null;
    }
}
