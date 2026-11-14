using System.Collections.Generic;
using System.IO;
using System.Linq;
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

/// <summary>
/// One selected mod and what a bulk update would do with it.
/// <see cref="FromPattern"/>: the folder was found through the session's folder pattern, not remembered.
/// </summary>
public sealed record BulkUpdateEntry(
    WorkshopItem Item, string? Folder, BulkUpdateState State, long SizeBytes, bool FromPattern = false);

public static class BulkUpdatePlanner
{
    /// <summary>
    /// Classifies every mod; those without a usable remembered folder are then looked for
    /// through <paramref name="pattern"/>, when there is one.
    /// </summary>
    public static IReadOnlyList<BulkUpdateEntry> Plan(
        IReadOnlyList<WorkshopItem> items, IReadOnlyDictionary<ulong, ItemFileInfo?> stored, string? pattern)
    {
        var plan = items.Select(i => Classify(i, stored.GetValueOrDefault(i.Id))).ToList();
        if (!ContentFolderPattern.IsValid(pattern)) return plan;

        var orphans = plan.Where(e => e.State is BulkUpdateState.NoFolder or BulkUpdateState.FolderMissing)
            .Select(e => e.Item).ToList();
        var found = ContentFolderPattern.Resolve(pattern!, orphans);

        return plan.Select(e => found.TryGetValue(e.Item.Id, out var folder)
            ? new BulkUpdateEntry(e.Item, folder, BulkUpdateState.Ready, ModFileInfoBuilder.InspectFolder(folder).Size, FromPattern: true)
            : e).ToList();
    }

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
