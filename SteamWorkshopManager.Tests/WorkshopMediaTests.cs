using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Workshop;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class WorkshopMediaTests
{
    [TestMethod]
    public void ImageExtensions_IncludesWebp()
    {
        CollectionAssert.Contains(WorkshopMedia.ImageExtensions, ".webp");
    }

    [TestMethod]
    public void ImageExtensions_ExcludesBmp()
    {
        CollectionAssert.DoesNotContain(WorkshopMedia.ImageExtensions, ".bmp");
    }

    [TestMethod]
    public void ImageExtensions_AreAllLowercaseDottedAndUnique()
    {
        foreach (var ext in WorkshopMedia.ImageExtensions)
        {
            Assert.IsTrue(ext.StartsWith('.'));
            Assert.AreEqual(ext.ToLowerInvariant(), ext);
        }

        var distinctCount = WorkshopMedia.ImageExtensions.Distinct().Count();
        Assert.AreEqual(WorkshopMedia.ImageExtensions.Length, distinctCount);
    }
}
