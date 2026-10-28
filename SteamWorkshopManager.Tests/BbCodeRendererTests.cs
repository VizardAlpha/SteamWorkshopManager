using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Helpers;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class BbCodeRendererTests
{
    [TestMethod]
    [DataRow("\n\nText", "\nText")]
    [DataRow("\r\n\r\nText", "\r\nText")]
    [DataRow("Text\n", "Text\n")]
    [DataRow("", "")]
    public void StripLeadingBreak_RemovesOnlyOneNewline(string input, string expected)
    {
        Assert.AreEqual(expected, BbCodeRenderer.StripLeadingBreak(input));
    }
}
