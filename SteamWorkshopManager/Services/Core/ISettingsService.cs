using System.Text.Json.Serialization;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services.Core;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();

    ItemFileInfo? GetContentFolderInfo(ulong publishedFileId);
    void SetContentFolderPath(ulong publishedFileId, string? path);
    void SetContentFolderInfo(ulong publishedFileId, ItemFileInfo? info);

    ItemFileInfo? GetPreviewImageInfo(ulong publishedFileId);
    void SetPreviewImagePath(ulong publishedFileId, string? path);
    void SetPreviewImageInfo(ulong publishedFileId, ItemFileInfo? info);
}

public class AppSettings
{
    public string Language { get; set; } = "en-US";
    public bool DebugMode { get; set; }

    /// <summary>
    /// ID of the currently active workshop session.
    /// </summary>
    public string? ActiveSessionId { get; set; }

    // Legacy plaintext Steam credentials, read once for migration to SteamCredentialStore then cleared.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SteamRefreshToken { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SteamAccessToken { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SteamAccountName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ulong SteamId64 { get; set; }

    /// <summary>Last app version whose release notes were shown, drives the "What's new" dialog.</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>
    /// Whether usage statistics are sent to swm-stats.com. Off by default so
    /// the consent screen starts unchecked - sending requires the user to
    /// explicitly opt in (see <see cref="TelemetryConsentVersion"/>).
    /// </summary>
    public bool TelemetryEnabled { get; set; }

    /// <summary>
    /// Tracks which version of the telemetry consent UI the user has been
    /// shown and acknowledged. 0 = never seen (legacy or fresh install).
    /// When the running build's required version is higher, the app must
    /// show the consent modal again before any telemetry is dispatched.
    /// Bumped whenever the data we collect or the destination changes.
    /// </summary>
    public int TelemetryConsentVersion { get; set; }

    /// <summary>
    /// When true, the update checker also considers GitHub pre-releases
    /// (beta channel). Off by default - most users only want stable builds.
    /// </summary>
    public bool IncludePrereleases { get; set; }

    /// <summary>
    /// Screen corner where toast notifications appear. Defaults to bottom-right
    /// so they stay clear of the top navigation bar.
    /// </summary>
    public ToastPosition ToastPosition { get; set; } = ToastPosition.BottomRight;

    /// <summary>
    /// How much activity detail is published to Discord. Off by default so
    /// nothing is shared until the user opts in.
    /// </summary>
    public DiscordPresenceMode DiscordPresenceMode { get; set; } = DiscordPresenceMode.Off;
}
