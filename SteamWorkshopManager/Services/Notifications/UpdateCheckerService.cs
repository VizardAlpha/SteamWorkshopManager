using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Notifications;

public static class UpdateCheckerService
{
    private static readonly Logger Log = new(nameof(UpdateCheckerService), LogService.Instance);

    public const string RepoUrl = "https://github.com/VizardAlpha/SteamWorkshopManager";
    private const string ApiBase = "https://api.github.com/repos/VizardAlpha/SteamWorkshopManager";
    // Drafts aren't returned by the public API, so the list is safe to scan as-is.
    private const string AllReleasesUrl = $"{ApiBase}/releases?per_page=30";

    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders =
        {
            { "User-Agent", $"SteamWorkshopManager/{AppInfo.Version}" },
            { "Accept", "application/vnd.github+json" }
        }
    };

    /// <summary>Release zip name for the running OS, matching the CI matrix.</summary>
    public static string? PlatformPackageName =>
        OperatingSystem.IsWindows() ? "SteamWorkshopManager-windows.zip"
        : OperatingSystem.IsMacOS() ? "SteamWorkshopManager-macos.zip"
        : OperatingSystem.IsLinux() ? "SteamWorkshopManager-linux.zip"
        : null;

    /// <summary>
    /// Returns an <see cref="UpdateInfo"/> when a newer release exists on the user's
    /// channel, else null. Stable channel: only final releases. Beta channel
    /// (<paramref name="includePrereleases"/>): alpha/beta/rc releases too.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(bool includePrereleases = false)
    {
        try
        {
            var releases = await Http.GetFromJsonAsync<GitHubRelease[]>(AllReleasesUrl) ?? [];
            var current = SemVersion.Parse(AppInfo.Version);
            if (current is null) return null;

            var best = PickLatest(releases, includePrereleases);
            if (best is null || best.Value.Version <= current) return null;

            return ToUpdateInfo(best.Value.Release);
        }
        catch (Exception ex)
        {
            // Non-critical
            Log.Debug($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Notes of the newest release on the user's channel, null when unavailable.</summary>
    public static async Task<ReleaseNotes?> GetLatestReleaseNotesAsync(bool includePrereleases)
    {
        try
        {
            var releases = await Http.GetFromJsonAsync<GitHubRelease[]>(AllReleasesUrl) ?? [];
            return PickLatest(releases, includePrereleases) is { } best ? ToNotes(best.Release) : null;
        }
        catch (Exception ex)
        {
            Log.Debug($"Release notes fetch failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Highest release allowed on the channel. A release counts as pre-release when GitHub
    /// flags it or its tag has a suffix (v1.9.0-beta.1), so a mis-flagged beta never reaches stable users.
    /// </summary>
    internal static (GitHubRelease Release, SemVersion Version)? PickLatest(
        IEnumerable<GitHubRelease> releases, bool includePrereleases)
    {
        (GitHubRelease Release, SemVersion Version)? best = null;
        foreach (var release in releases)
        {
            if (SemVersion.Parse(release.TagName) is not { } version) continue;
            var isPre = release.Prerelease || version.IsPreRelease;
            if (isPre && !includePrereleases) continue;
            if (best is null || version > best.Value.Version) best = (release, version);
        }
        return best;
    }

    /// <summary>
    /// Notes of the given version. Returns null on network failure (retry later)
    /// and an empty-markdown entry when the tag has no release.
    /// </summary>
    public static async Task<ReleaseNotes?> GetReleaseNotesAsync(string version)
    {
        try
        {
            using var response = await Http.GetAsync($"{ApiBase}/releases/tags/v{Uri.EscapeDataString(version)}");
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new ReleaseNotes(version, null, string.Empty, false);
            response.EnsureSuccessStatusCode();

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
            return release is null ? null : ToNotes(release);
        }
        catch (Exception ex)
        {
            Log.Debug($"Release notes fetch for {version} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Only pages of this repo may be opened from API data.</summary>
    public static bool IsTrustedReleasePageUrl(string? url) =>
        url is not null && url.StartsWith($"{RepoUrl}/releases/", StringComparison.Ordinal)
                        && Uri.TryCreate(url, UriKind.Absolute, out _);

    /// <summary>Only release assets of this repo may be downloaded (GitHub then redirects to its CDN).</summary>
    public static bool IsTrustedAssetUrl(string? url) =>
        url is not null && url.StartsWith($"{RepoUrl}/releases/download/", StringComparison.Ordinal)
                        && Uri.TryCreate(url, UriKind.Absolute, out _);

    internal static UpdateInfo ToUpdateInfo(GitHubRelease release)
    {
        var package = release.Assets.FirstOrDefault(a => a.Name == PlatformPackageName && IsTrustedAssetUrl(a.BrowserDownloadUrl));
        var checksums = release.Assets.FirstOrDefault(a => a.Name == ChecksumsAssetName && IsTrustedAssetUrl(a.BrowserDownloadUrl));

        return new UpdateInfo(
            AppInfo.Version,
            release.TagName,
            IsTrustedReleasePageUrl(release.HtmlUrl) ? release.HtmlUrl : $"{RepoUrl}/releases",
            release.Body ?? string.Empty,
            package?.Name,
            package?.BrowserDownloadUrl,
            package?.Size ?? 0,
            checksums?.BrowserDownloadUrl);
    }

    private static ReleaseNotes ToNotes(GitHubRelease r) =>
        new(r.TagName.TrimStart('v'), r.PublishedAt, r.Body ?? string.Empty,
            r.Prerelease || SemVersion.Parse(r.TagName)?.IsPreRelease == true);
}
