using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Session;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class SessionSerializerTests
{
    [TestMethod]
    public void LegacyFileWithoutPlatform_IsReadAsSteamSession()
    {
        const string json = """
            {
              "Id": "abc",
              "Name": "Songs of Syx",
              "AppId": 1162750,
              "CustomTags": ["Mine"]
            }
            """;

        var session = SessionSerializer.Deserialize(json);

        var steam = session as SteamSession;
        Assert.IsNotNull(steam);
        Assert.AreEqual("abc", steam.Id);
        Assert.AreEqual(1162750U, steam.AppId);
        Assert.AreEqual("Mine", steam.CustomTags[0]);
    }

    [TestMethod]
    public void SteamSession_RoundTripsWithPlatform()
    {
        var json = SessionSerializer.Serialize(new SteamSession { Id = "s", AppId = 294100 });

        StringAssert.Contains(json, "\"Platform\": \"steam\"");
        Assert.AreEqual(294100U, ((SteamSession)SessionSerializer.Deserialize(json)!).AppId);
    }

    [TestMethod]
    public void ModioSession_RoundTrips()
    {
        var json = SessionSerializer.Serialize(new ModioSession { Id = "m", GameId = 5, GameNameId = "syx" });

        var modio = SessionSerializer.Deserialize(json) as ModioSession;

        Assert.IsNotNull(modio);
        Assert.AreEqual(5U, modio.GameId);
        Assert.AreEqual("syx", modio.GameNameId);
        Assert.AreEqual(WorkshopPlatform.ModIo, modio.Platform);
    }

    [TestMethod]
    public void SameGame_RequiresSamePlatformAndId()
    {
        var steam = new SteamSession { AppId = 5 };

        Assert.IsTrue(steam.IsSameGame(new SteamSession { AppId = 5 }));
        Assert.IsFalse(steam.IsSameGame(new SteamSession { AppId = 6 }));
        Assert.IsFalse(steam.IsSameGame(new ModioSession { GameId = 5 }));
    }

    [TestMethod]
    public void SessionScope_DependsOnPlatform()
    {
        Assert.AreEqual((CacheKey.Steam, "app-5"), CacheKey.SessionScope(new SteamSession { AppId = 5 }));
        Assert.AreEqual((CacheKey.ModIo, "game-5"), CacheKey.SessionScope(new ModioSession { GameId = 5 }));
    }
}
