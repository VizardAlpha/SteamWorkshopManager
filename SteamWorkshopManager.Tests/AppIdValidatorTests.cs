using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Steam;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class AppIdValidatorTests
{
    [TestMethod]
    [DataRow("440", 440U)]
    [DataRow("  570  ", 570U)]
    [DataRow("https://store.steampowered.com/app/620/Portal_2/", 620U)]
    [DataRow("https://steamcommunity.com/app/2555430/workshop/", 2555430U)]
    [DataRow("steam://store/294100", 294100U)]
    public void TryParseAppId_Valid_ReturnsTrueAndId(string input, uint expectedId)
    {
        var ok = AppIdValidator.TryParseAppId(input, out var appId);
        Assert.IsTrue(ok);
        Assert.AreEqual(expectedId, appId);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("abc")]
    [DataRow("0")]
    public void TryParseAppId_Invalid_ReturnsFalse(string input)
    {
        var ok = AppIdValidator.TryParseAppId(input, out var appId);
        Assert.IsFalse(ok);
        Assert.AreEqual(0U, appId);
    }
}
