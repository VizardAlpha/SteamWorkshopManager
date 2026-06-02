using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Workshop;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class WorkshopInputParserTests
{
    [TestMethod]
    [DataRow("dQw4w9WgXcQ", "dQw4w9WgXcQ")]                                  // bare 11-char id
    [DataRow("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]  // watch URL
    [DataRow("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]                 // short link
    [DataRow("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]    // embed
    [DataRow("  dQw4w9WgXcQ  ", "dQw4w9WgXcQ")]                              // trimmed
    public void ParseYouTubeId_ValidInputs_ReturnsId(string input, string expected)
    {
        Assert.AreEqual(expected, WorkshopInputParser.ParseYouTubeId(input));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not a youtube id")]
    [DataRow("https://example.com/watch?v=tooShort")]
    public void ParseYouTubeId_InvalidInputs_ReturnsNull(string input)
    {
        Assert.IsNull(WorkshopInputParser.ParseYouTubeId(input));
    }

    [TestMethod]
    public void ParseYouTubeId_Null_ReturnsNull()
    {
        Assert.IsNull(WorkshopInputParser.ParseYouTubeId(null));
    }

    [TestMethod]
    [DataRow("123456789", 123456789UL)]
    [DataRow("https://steamcommunity.com/sharedfiles/filedetails/?id=987654321", 987654321UL)]
    [DataRow("  42  ", 42UL)]
    public void ParseWorkshopId_ValidInputs_ReturnsId(string input, ulong expected)
    {
        Assert.AreEqual(expected, WorkshopInputParser.ParseWorkshopId(input));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("no digits here")]
    public void ParseWorkshopId_InvalidInputs_ReturnsZero(string input)
    {
        Assert.AreEqual(0UL, WorkshopInputParser.ParseWorkshopId(input));
    }
}
