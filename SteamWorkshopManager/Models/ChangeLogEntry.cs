using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SteamWorkshopManager.Models;

public partial class ChangeLogEntry : ObservableObject
{
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("change_description")]
    public string ChangeDescription { get; set; } = string.Empty;

    [JsonPropertyName("manifest_id")]
    public string ManifestId { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public int Language { get; set; }

    [JsonPropertyName("saved_snapshot")]
    public string? SavedSnapshot { get; set; }

    [JsonPropertyName("snapshot_gamebranch_min")]
    public string? SnapshotGameBranchMin { get; set; }

    [JsonPropertyName("snapshot_gamebranch_max")]
    public string? SnapshotGameBranchMax { get; set; }

    [JsonPropertyName("accountid")]
    public long AccountId { get; set; }

    [JsonIgnore]
    public DateTime Date => DateTimeOffset.FromUnixTimeSeconds(Timestamp).LocalDateTime;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isDownloaded;

    [JsonIgnore]
    public bool HasManifestId => !string.IsNullOrEmpty(ManifestId);

    [JsonIgnore]
    public bool HasBranches => !string.IsNullOrEmpty(SnapshotGameBranchMin) || !string.IsNullOrEmpty(SnapshotGameBranchMax);
}
