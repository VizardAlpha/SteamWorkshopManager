using System.IO;
using WorkshopManager.Models;

namespace WorkshopManager.Core.Workshop;

public enum BulkUpdateState
{
    /// <summary>The content folder changed since the last upload.</summary>
    Ready,
    /// <summary>Same size and dates as the last upload: nothing to send.</summary>
    Unchanged,
    /// <summary>No content folder was ever chosen for this mod in the app.</summary>
    NoFolder,
    /// <summary>The remembered folder no longer exists.</summary>
    FolderMissing,
}

/// <summary>One selected mod and what a bulk update would do with it.</summary>
public sealed record BulkUpdateEntry(WorkshopItem Item, string? Folder, BulkUpdateState State, long SizeBytes);

public static class BulkUpdatePlanner
{
    /// <summary><paramref name="stored"/> is what the app recorded at the mod's last upload.</summary>
    public static BulkUpdateEntry Classify(WorkshopItem item, ItemFileInfo? stored)
    {
        if (stored is null || string.IsNullOrWhiteSpace(stored.Path))
            return new BulkUpdateEntry(item, null, BulkUpdateState.NoFolder, 0);

        if (!Directory.Exists(stored.Path))
            return new BulkUpdateEntry(item, stored.Path, BulkUpdateState.FolderMissing, 0);

        var now = ModFileInfoBuilder.InspectFolder(stored.Path);
        // A folder only picked, never uploaded, has no recorded size: it counts as changed.
        var unchanged = stored.Size > 0 && stored.Size == now.Size && stored.LastModifiedUtc == now.LastModifiedUtc;
        return new BulkUpdateEntry(item, stored.Path, unchanged ? BulkUpdateState.Unchanged : BulkUpdateState.Ready, now.Size);
    }
}
