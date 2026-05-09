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

    /// <summary>
    /// SteamKit2 refresh token for web authentication (~200 day lifetime).
    /// </summary>
    public string? SteamRefreshToken { get; set; }

    /// <summary>
    /// SteamKit2 access token (JWT, ~24h lifetime). Persisted to avoid CM reconnect on restart.
    /// </summary>
    public string? SteamAccessToken { get; set; }

    /// <summary>
    /// Steam account name associated with the refresh token.
    /// </summary>
    public string? SteamAccountName { get; set; }

    /// <summary>
    /// Steam ID 64 associated with the refresh token.
    /// </summary>
    public ulong SteamId64 { get; set; }

    /// <summary>
    /// Whether usage statistics are sent to swm-stats.com. Off by default so
    /// the consent screen starts unchecked — sending requires the user to
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
}
