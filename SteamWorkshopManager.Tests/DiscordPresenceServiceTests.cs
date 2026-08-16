using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Presence;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class DiscordPresenceServiceTests
{
    private static readonly DateTime StartedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PresenceState State(
        ShellTab tab = ShellTab.MyMods,
        string? game = "Songs of Syx",
        uint appId = 1162750,
        string? title = "Electrum",
        bool uploading = false) => new(tab, game, appId, title, uploading);

    private static DiscordRPC.RichPresence Build(PresenceState state, DiscordPresenceMode mode) =>
        DiscordPresenceService.BuildPresence(state, mode, StartedAt);

    [TestMethod]
    public void Minimal_ShowsTheSectionButHidesGameAndItem()
    {
        var presence = Build(State(), DiscordPresenceMode.Minimal);

        Assert.AreEqual("Browsing Workshop items", presence.Details);
        Assert.IsTrue(string.IsNullOrEmpty(presence.State));
        Assert.AreEqual("app_logo", presence.Assets.LargeImageKey);
    }

    [TestMethod]
    [DataRow(ShellTab.Create, "Creating a new item")]
    [DataRow(ShellTab.Settings, "Configuring the app")]
    [DataRow(ShellTab.Home, "Managing Workshop items")]
    public void Minimal_FollowsTheSectionLikeTheOtherModes(ShellTab tab, string expected)
    {
        var presence = Build(State(tab: tab), DiscordPresenceMode.Minimal);

        Assert.AreEqual(expected, presence.Details);
        Assert.IsTrue(string.IsNullOrEmpty(presence.State));
    }

    [TestMethod]
    public void Game_ShowsGameButNeverItemTitle()
    {
        var presence = Build(State(), DiscordPresenceMode.Game);

        Assert.AreEqual("Songs of Syx", presence.State);
        Assert.AreEqual("Browsing Workshop items", presence.Details);
    }

    [TestMethod]
    public void Detailed_ShowsItemTitle()
    {
        var presence = Build(State(), DiscordPresenceMode.Detailed);

        Assert.AreEqual("Editing Electrum", presence.Details);
        Assert.AreEqual("Songs of Syx", presence.State);
    }

    [TestMethod]
    public void Detailed_WithoutItem_FallsBackToSection()
    {
        var presence = Build(State(title: null), DiscordPresenceMode.Detailed);

        Assert.AreEqual("Browsing Workshop items", presence.Details);
    }

    [TestMethod]
    public void Uploading_TakesPrecedenceOverSection()
    {
        var detailed = Build(State(uploading: true), DiscordPresenceMode.Detailed);
        var game = Build(State(uploading: true), DiscordPresenceMode.Game);

        Assert.AreEqual("Uploading Electrum", detailed.Details);
        Assert.AreEqual("Uploading to the Workshop", game.Details);
    }

    [TestMethod]
    [DataRow(ShellTab.Create, "Creating a new item")]
    [DataRow(ShellTab.Settings, "Configuring the app")]
    [DataRow(ShellTab.Home, "Managing Workshop items")]
    public void Sections_MapToTheirOwnWording(ShellTab tab, string expected)
    {
        var presence = Build(State(tab: tab, title: null), DiscordPresenceMode.Game);

        Assert.AreEqual(expected, presence.Details);
    }

    [TestMethod]
    public void Assets_NameTheGameInTheLogoHoverText()
    {
        var presence = Build(State(), DiscordPresenceMode.Game);

        // Asset keys are capped at 32 chars, so the key stays the uploaded logo.
        Assert.AreEqual("app_logo", presence.Assets.LargeImageKey);
        Assert.AreEqual("Songs of Syx", presence.Assets.LargeImageText);
    }

    [TestMethod]
    public void Assets_FallBackToTheAppNameWithoutASession()
    {
        var presence = Build(State(game: null, appId: 0), DiscordPresenceMode.Game);

        Assert.AreEqual("app_logo", presence.Assets.LargeImageKey);
        Assert.AreEqual("Steam Workshop Manager", presence.Assets.LargeImageText);
        Assert.IsTrue(string.IsNullOrEmpty(presence.State));
    }

    [TestMethod]
    public void Assets_KeepTheGameOutOfMinimalMode()
    {
        var presence = Build(State(), DiscordPresenceMode.Minimal);

        Assert.AreEqual("Steam Workshop Manager", presence.Assets.LargeImageText);
    }

    [TestMethod]
    public void LongTitle_IsTruncatedToDiscordsLimit()
    {
        // Discord rejects fields over 128 bytes, so a pathological title must be cut.
        var presence = Build(State(title: new string('a', 400)), DiscordPresenceMode.Detailed);

        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(presence.Details) <= 128);
        StringAssert.EndsWith(presence.Details, "…");
    }

    [TestMethod]
    public void MultiByteTitle_IsTruncatedWithoutSplittingACharacter()
    {
        var presence = Build(State(title: string.Concat(Enumerable.Repeat("é", 200))), DiscordPresenceMode.Detailed);

        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(presence.Details) <= 128);
        // A split UTF-8 sequence would surface as the replacement character.
        Assert.IsFalse(presence.Details.Contains('�'));
    }

    [TestMethod]
    public void Timestamp_ReportsTheAppStartTime()
    {
        var presence = Build(State(), DiscordPresenceMode.Game);

        Assert.AreEqual(StartedAt, presence.Timestamps.Start);
    }
}
