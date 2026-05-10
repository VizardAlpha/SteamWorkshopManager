using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using Steamworks;

namespace SteamWorkshopManager.Services.Steam;

public enum SteamInitResult
{
    Success,
    SteamNotRunning,
    GameNotOwned
}

/// <summary>
/// One mutation applied to a Workshop item's additional-previews list as
/// part of an update. Composed by the editor view-model from the diff
/// between the item's last-known Steam state and the user's edits.
/// </summary>
public abstract record PreviewOp
{
    /// <summary>Remove an existing preview by its current Steam-side index.</summary>
    public sealed record Remove(uint Index) : PreviewOp;

    /// <summary>Append a new image preview from a local file path.</summary>
    public sealed record AddImage(string FilePath) : PreviewOp;

    /// <summary>Append a new YouTube video preview by its 11-character video ID.</summary>
    public sealed record AddVideo(string YouTubeId) : PreviewOp;
}

public interface ISteamService
{
    bool IsInitialized { get; }
    CSteamID? CurrentUserId { get; }

    SteamInitResult Initialize();
    void Shutdown();

    Task<List<WorkshopItem>> GetPublishedItemsAsync();

    /// <summary>
    /// Fetches a single Workshop item by id. Returns null when Steam can't
    /// resolve it (e.g. brief indexing latency right after a Create).
    /// </summary>
    Task<WorkshopItem?> GetPublishedItemAsync(PublishedFileId_t fileId);
    Task<PublishedFileId_t?> CreateItemAsync(string title, string description, string contentFolderPath,
        string? previewImagePath, VisibilityType visibility, List<string> tags, string? changelog,
        IProgress<UploadProgress>? progress = null, string? branchMin = null, string? branchMax = null,
        IReadOnlyList<PreviewOp>? previewOps = null);
    Task<bool> UpdateItemAsync(PublishedFileId_t fileId, string? title, string? description,
        string? contentFolderPath, string? previewImagePath, VisibilityType? visibility,
        List<string>? tags, string? changelog, IProgress<UploadProgress>? progress = null,
        string? branchMin = null, string? branchMax = null,
        IReadOnlyList<PreviewOp>? previewOps = null);
    Task<bool> DeleteItemAsync(PublishedFileId_t fileId);

    /// <summary>
    /// Gets the list of game branches (betas) available for the current AppId.
    /// Returns an empty list if versioning is not enabled.
    /// </summary>
    List<GameBranch> GetGameBranches();

    /// <summary>
    /// Gets the current active beta branch name. Returns "public" if on the default branch.
    /// </summary>
    string GetCurrentBranchName();
}

public record UploadProgress(string Status, ulong BytesProcessed, ulong BytesTotal, double PercentHint = 0)
{
    /// <summary>
    /// Percentage to drive the progress bar. When Steam is reporting real
    /// bytes (e.g. content upload phase), use the bytes ratio. Otherwise
    /// fall back to the explicit hint set by the call site — keeps phases
    /// like "Preparing" / "Committing" advancing the bar without forcing
    /// the UI to invent fake byte counts.
    /// </summary>
    public double Percentage => BytesTotal > 0 ? (double)BytesProcessed / BytesTotal * 100 : PercentHint;
}
