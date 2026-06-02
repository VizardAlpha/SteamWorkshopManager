using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Workshop;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ModValidatorTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ValidateForCreate_BlankTitle_Fails(string title)
    {
        var result = ModValidator.ValidateForCreate(title, Path.GetTempPath());
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("TitleRequired", result.ErrorKey);
    }

    [TestMethod]
    public void ValidateForCreate_BlankFolder_Fails()
    {
        var result = ModValidator.ValidateForCreate("Title", "");
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("FolderRequired", result.ErrorKey);
    }

    [TestMethod]
    public void ValidateForCreate_MissingFolder_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var result = ModValidator.ValidateForCreate("Title", missing);
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("FolderNotExist", result.ErrorKey);
    }

    [TestMethod]
    public void ValidateForCreate_ValidInputs_Ok()
    {
        var result = ModValidator.ValidateForCreate("Title", Path.GetTempPath());
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorKey);
    }

    [TestMethod]
    public void ValidateForUpdate_BlankTitle_Fails()
    {
        Assert.IsFalse(ModValidator.ValidateForUpdate(" ").IsValid);
    }

    [TestMethod]
    public void ValidateForUpdate_ValidTitle_Ok()
    {
        Assert.IsTrue(ModValidator.ValidateForUpdate("My Mod").IsValid);
    }

    [TestMethod]
    public async Task IsImageTooLarge_LargeFile_True()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[ModValidator.MaxImageSizeBytes + 1]);
            Assert.IsTrue(ModValidator.IsImageTooLarge(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task IsImageTooLarge_SmallFile_False()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[16]);
            Assert.IsFalse(ModValidator.IsImageTooLarge(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void IsImageTooLarge_MissingFile_False()
    {
        Assert.IsFalse(ModValidator.IsImageTooLarge("definitely-not-here.png"));
    }
}
