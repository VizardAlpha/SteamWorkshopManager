using System.Collections.Generic;

namespace SteamWorkshopManager.Services.Interfaces;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();
    void Load();

    string? GetContentFolderPath(ulong publishedFileId);
    void SetContentFolderPath(ulong publishedFileId, string? path);

    string? GetPreviewImagePath(ulong publishedFileId);
    void SetPreviewImagePath(ulong publishedFileId, string? path);

    IReadOnlyList<string> GetCustomTags();
    void AddCustomTag(string tag);
    void RemoveCustomTag(string tag);
}

public class AppSettings
{
    public string Language { get; set; } = "en-US";
    public bool DebugMode { get; set; }

    /// <summary>
    /// ID of the currently active workshop session.
    /// </summary>
    public string? ActiveSessionId { get; set; }

    // Legacy paths - will be migrated to sessions
    public Dictionary<string, string> ContentFolderPaths { get; set; } = new();
    public Dictionary<string, string> PreviewImagePaths { get; set; } = new();
    public List<string> CustomTags { get; set; } = [];

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
}
