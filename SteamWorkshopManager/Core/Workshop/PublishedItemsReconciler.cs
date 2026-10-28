using System.Collections.Generic;
using System.Linq;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>Outcome of merging Steam's list into the one on screen.</summary>
/// <param name="Next">Steam's order; unchanged items keep their on-screen instance.</param>
/// <param name="Replaced">On-screen items whose Steam version differs, with that version.</param>
/// <param name="Removed">On-screen items Steam no longer lists.</param>
public sealed record ItemsReconciliation(
    List<WorkshopItem> Next,
    List<(WorkshopItem Old, WorkshopItem Fresh)> Replaced,
    List<WorkshopItem> Removed);

/// <summary>
/// Merges a fresh item list into the displayed one. Unchanged items keep their instance
/// (thumbnail, selection, open editor stay put); changed ones are swapped.
/// </summary>
public static class PublishedItemsReconciler
{
    public static ItemsReconciliation Reconcile(IReadOnlyList<WorkshopItem> current, IReadOnlyList<WorkshopItem> fresh)
    {
        var byId = new Dictionary<ulong, WorkshopItem>();
        foreach (var item in current) byId.TryAdd((ulong)item.PublishedFileId, item);

        var next = new List<WorkshopItem>(fresh.Count);
        var replaced = new List<(WorkshopItem, WorkshopItem)>();

        foreach (var item in fresh)
        {
            if (!byId.Remove((ulong)item.PublishedFileId, out var existing))
            {
                next.Add(item);
            }
            else if (SameContent(existing, item))
            {
                next.Add(existing);
            }
            else
            {
                item.IsSelected = existing.IsSelected;
                next.Add(item);
                replaced.Add((existing, item));
            }
        }

        return new ItemsReconciliation(next, replaced, byId.Values.ToList());
    }

    /// <summary>Everything the list and the editor show; the bitmap and UI flags are not content.</summary>
    public static bool SameContent(WorkshopItem a, WorkshopItem b) =>
        a.Title == b.Title
        && a.Description == b.Description
        && a.Visibility == b.Visibility
        && a.UpdatedAt == b.UpdatedAt
        && a.PreviewImageUrl == b.PreviewImageUrl
        && a.PreviewImagePath == b.PreviewImagePath
        && a.SubscriberCount == b.SubscriberCount
        && a.FileSize == b.FileSize
        && a.IsOwner == b.IsOwner
        && a.Tags.Select(t => t.Name).SequenceEqual(b.Tags.Select(t => t.Name))
        && a.AdditionalPreviews.Select(p => (p.PreviewType, p.OriginalIndex, p.RemoteUrl))
            .SequenceEqual(b.AdditionalPreviews.Select(p => (p.PreviewType, p.OriginalIndex, p.RemoteUrl)));
}
