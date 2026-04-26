using System;
using System.Collections.Generic;

namespace SteamWorkshopManager.Models;

/// <summary>
/// Persisted state for an in-progress mod creation. Each draft lives on disk
/// under <c>%AppData%/SteamWorkshopManager/Tempo/&lt;TempId&gt;/draft.json</c>.
/// The <see cref="TempId"/> is a compact Guid that doubles as the folder name,
/// so re-loading a draft is just "open this folder". On publish the entire
/// folder is removed.
/// </summary>
public sealed record CreateDraft(
    string TempId,
    uint AppId,
    string Title,
    string Description,
    string? ContentFolderPath,
    string? PreviewImagePath,
    VisibilityType Visibility,
    string InitialChangelog,
    bool TargetAllVersions,
    string? BranchMin,
    string? BranchMax,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<string>? CustomTags = null,
    List<string>? SelectedTags = null
)
{
    /// <summary>Display label fallback when the draft has no title yet.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? "Untitled draft" : Title;
}
