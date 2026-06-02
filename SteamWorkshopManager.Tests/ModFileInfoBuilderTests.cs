using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Workshop;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ModFileInfoBuilderTests
{
    [TestMethod]
    public async Task InspectFile_ExistingFile_ReturnsSize()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[128]);
            Assert.AreEqual(128L, ModFileInfoBuilder.InspectFile(path).Size);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void InspectFile_NullOrMissing_ReturnsEmpty()
    {
        Assert.AreEqual(0L, ModFileInfoBuilder.InspectFile(null).Size);
        Assert.AreEqual(0L, ModFileInfoBuilder.InspectFile("missing.bin").Size);
    }

    [TestMethod]
    public async Task InspectFolder_SumsFileSizesRecursively()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var sub = Directory.CreateDirectory(Path.Combine(dir, "sub"));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "a.bin"), new byte[100]);
            await File.WriteAllBytesAsync(Path.Combine(sub.FullName, "b.bin"), new byte[50]);
            Assert.AreEqual(150L, ModFileInfoBuilder.InspectFolder(dir).Size);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void InspectFolder_NullOrMissing_ReturnsEmpty()
    {
        Assert.AreEqual(0L, ModFileInfoBuilder.InspectFolder(null).Size);
        Assert.AreEqual(0L, ModFileInfoBuilder.InspectFolder("no-such-dir").Size);
    }
}
