using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Workshop;
using Steamworks;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class PublishedItemsTests
{
    private static readonly DateTime Updated = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static WorkshopItem Item(ulong id, string title = "Mod", ulong subs = 10) => new()
    {
        PublishedFileId = new PublishedFileId_t(id),
        Title = title,
        Description = "desc",
        PreviewImageUrl = $"https://cdn/{id}.jpg",
        Visibility = VisibilityType.Public,
        Tags = [new WorkshopTag("Maps")],
        AdditionalPreviews =
        [
            new WorkshopPreview
            {
                Source = WorkshopPreviewSource.Existing,
                PreviewType = EItemPreviewType.k_EItemPreviewType_YouTubeVideo,
                OriginalIndex = 0,
                RemoteUrl = "abc123",
            },
        ],
        CreatedAt = Updated.AddDays(-3),
        UpdatedAt = Updated,
        SubscriberCount = subs,
        FileSize = 2048,
        OwnerId = new CSteamID(76561198000000000),
        IsOwner = true,
    };

    [TestMethod]
    public void Reconcile_KeepsUnchangedInstances()
    {
        var shown = Item(1);
        var outcome = PublishedItemsReconciler.Reconcile([shown], [Item(1)]);

        Assert.AreSame(shown, outcome.Next.Single());
        Assert.AreEqual(0, outcome.Replaced.Count);
        Assert.AreEqual(0, outcome.Removed.Count);
    }

    [TestMethod]
    public void Reconcile_SwapsChangedItemsAndKeepsSelection()
    {
        var shown = Item(1);
        shown.IsSelected = true;
        var fresh = Item(1, title: "Renamed");

        var outcome = PublishedItemsReconciler.Reconcile([shown], [fresh]);

        Assert.AreSame(fresh, outcome.Next.Single());
        Assert.AreSame(shown, outcome.Replaced.Single().Old);
        Assert.IsTrue(fresh.IsSelected);
    }

    [TestMethod]
    public void Reconcile_StatsChangeCountsAsChange()
    {
        var outcome = PublishedItemsReconciler.Reconcile([Item(1, subs: 10)], [Item(1, subs: 11)]);

        Assert.AreEqual(1, outcome.Replaced.Count);
    }

    [TestMethod]
    public void Reconcile_FollowsSteamOrderAndReportsRemovedAndNew()
    {
        var first = Item(1);
        var gone = Item(2);
        var outcome = PublishedItemsReconciler.Reconcile([first, gone], [Item(3), Item(1)]);

        CollectionAssert.AreEqual(new ulong[] { 3, 1 }, outcome.Next.Select(i => (ulong)i.PublishedFileId).ToArray());
        Assert.AreSame(first, outcome.Next[1]);
        Assert.AreSame(gone, outcome.Removed.Single());
        Assert.AreEqual(0, outcome.Replaced.Count);
    }

    [TestMethod]
    public async Task Cache_RoundTripsItemsPerAccount()
    {
        var root = Path.Combine(Path.GetTempPath(), "swm-items-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CacheStore(root, enabled: true, limitBytes: null);
            await store.Initialization;
            var cache = new PublishedItemsCache(store);

            await cache.SaveAsync(294100, 7, [Item(1), Item(2, title: "Second")]);
            var loaded = await cache.LoadAsync(294100, 7);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(2, loaded.Count);
            Assert.IsTrue(PublishedItemsReconciler.SameContent(Item(1), loaded[0]));
            Assert.AreEqual("Second", loaded[1].Title);
            Assert.IsNull(await cache.LoadAsync(294100, 8));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
