using Microsoft.VisualStudio.TestTools.UnitTesting;
using Steamworks;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.ViewModels.Editor;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class PreviewOpBuilderTests
{
    [TestMethod]
    public void BuildForCreate_EmitsImagesThenVideosInListOrder()
    {
        var images = new[] { NewImage(@"C:\shots\b.png"), NewImage(@"C:\shots\a.png") };
        var videos = new[] { NewVideo("dQw4w9WgXcQ"), NewVideo("9bZkp7q19f0") };

        var ops = PreviewOpBuilder.BuildForCreate(images, videos);

        Assert.AreSequenceEqual(
            new PreviewOp[]
            {
                new PreviewOp.AddImage(@"C:\shots\b.png"),
                new PreviewOp.AddImage(@"C:\shots\a.png"),
                new PreviewOp.AddVideo("dQw4w9WgXcQ"),
                new PreviewOp.AddVideo("9bZkp7q19f0"),
            },
            ops);
    }

    [TestMethod]
    public void BuildForCreate_EmptyInputs_ReturnsEmptyList()
    {
        var ops = PreviewOpBuilder.BuildForCreate([], []);

        Assert.AreEqual(0, ops.Count);
    }

    [TestMethod]
    public void AppendNewPreviewOp_ExistingEntry_IsIgnored()
    {
        var ops = new List<PreviewOp>();

        PreviewOpBuilder.AppendNewPreviewOp(ops, Existing(0));

        Assert.AreEqual(0, ops.Count);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void AppendNewPreviewOp_NewImageWithoutPath_IsIgnored(string? path)
    {
        var ops = new List<PreviewOp>();

        PreviewOpBuilder.AppendNewPreviewOp(ops, NewImage(path));

        Assert.AreEqual(0, ops.Count);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void AppendNewPreviewOp_NewVideoWithoutId_IsIgnored(string? videoId)
    {
        var ops = new List<PreviewOp>();

        PreviewOpBuilder.AppendNewPreviewOp(ops, NewVideo(videoId));

        Assert.AreEqual(0, ops.Count);
    }

    [TestMethod]
    public void AppendNewPreviewOp_AppendsAfterExistingOps()
    {
        var ops = new List<PreviewOp> { new PreviewOp.Remove(2) };

        PreviewOpBuilder.AppendNewPreviewOp(ops, NewImage(@"C:\shots\new.jpg"));

        Assert.AreSequenceEqual(
            new PreviewOp[] { new PreviewOp.Remove(2), new PreviewOp.AddImage(@"C:\shots\new.jpg") },
            ops);
    }

    [TestMethod]
    public void IsListOutOfOrder_EmptyList_IsInOrder()
    {
        Assert.IsFalse(PreviewGalleryViewModel.IsListOutOfOrder([]));
    }

    [TestMethod]
    public void IsListOutOfOrder_ExistingAscendingThenNew_IsInOrder()
    {
        var list = new[] { Existing(0), Existing(1), Existing(3), NewImage(@"C:\a.png"), NewVideo("abc") };

        Assert.IsFalse(PreviewGalleryViewModel.IsListOutOfOrder(list));
    }

    [TestMethod]
    public void IsListOutOfOrder_ExistingWithGapsAfterRemoval_IsInOrder()
    {
        var list = new[] { Existing(1), Existing(4) };

        Assert.IsFalse(PreviewGalleryViewModel.IsListOutOfOrder(list));
    }

    [TestMethod]
    public void IsListOutOfOrder_OnlyNewEntries_IsInOrder()
    {
        var list = new[] { NewImage(@"C:\b.png"), NewImage(@"C:\a.png") };

        Assert.IsFalse(PreviewGalleryViewModel.IsListOutOfOrder(list));
    }

    [TestMethod]
    public void IsListOutOfOrder_NewBeforeExisting_IsOutOfOrder()
    {
        var list = new[] { NewImage(@"C:\a.png"), Existing(0) };

        Assert.IsTrue(PreviewGalleryViewModel.IsListOutOfOrder(list));
    }

    [TestMethod]
    public void IsListOutOfOrder_NewBetweenExisting_IsOutOfOrder()
    {
        var list = new[] { Existing(0), NewImage(@"C:\a.png"), Existing(1) };

        Assert.IsTrue(PreviewGalleryViewModel.IsListOutOfOrder(list));
    }

    [TestMethod]
    public void IsListOutOfOrder_ExistingDescending_IsOutOfOrder()
    {
        var list = new[] { Existing(0), Existing(2), Existing(1) };

        Assert.IsTrue(PreviewGalleryViewModel.IsListOutOfOrder(list));
    }

    private static WorkshopPreview Existing(uint index) => new()
    {
        Source = WorkshopPreviewSource.Existing,
        PreviewType = EItemPreviewType.k_EItemPreviewType_Image,
        OriginalIndex = index,
        RemoteUrl = $"https://images.steamusercontent.com/ugc/{index}/",
    };

    private static WorkshopPreview NewImage(string? path) => new()
    {
        Source = WorkshopPreviewSource.NewImage,
        PreviewType = EItemPreviewType.k_EItemPreviewType_Image,
        LocalPath = path,
    };

    private static WorkshopPreview NewVideo(string? videoId) => new()
    {
        Source = WorkshopPreviewSource.NewVideo,
        PreviewType = EItemPreviewType.k_EItemPreviewType_YouTubeVideo,
        VideoId = videoId,
    };
}
