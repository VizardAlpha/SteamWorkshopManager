using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services;

public static class UpdateCheckerService
{
    private const string ReleasesUrl = "https://api.github.com/repos/VizardAlpha/SteamWorkshopManager/releases/latest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders =
        {
            { "User-Agent", $"SteamWorkshopManager/{AppInfo.Version}" },
            { "Accept", "application/vnd.github+json" }
        }
    };

    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(ReleasesUrl);
            if (release is null || release.Prerelease) return null;

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
            // Non-critical — silently fail
            return null;
        }
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
