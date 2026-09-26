using System;

namespace SteamWorkshopManager.Models;

/// <param name="PackageUrl">Zip for this platform, null when the release has none (manual download only).</param>
/// <param name="ChecksumsUrl">SHA256SUMS.txt asset, required for the in-app install.</param>
public record UpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string ReleaseNotes,
    string? PackageName = null,
    string? PackageUrl = null,
    long PackageSize = 0,
    string? ChecksumsUrl = null)
{
    public bool CanInstallInApp => PackageUrl is not null && ChecksumsUrl is not null;
}

/// <summary>One entry of the in-app release notes list.</summary>
public record ReleaseNotes(string Version, DateTime? PublishedAt, string Markdown, bool IsPrerelease);
