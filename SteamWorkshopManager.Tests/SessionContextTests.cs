using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class SessionContextTests
{
    [TestMethod]
    public void NewContext_HasNoSessionAndZeroAppId()
    {
        var context = new SessionContext();

        Assert.IsNull(context.Current);
        Assert.AreEqual(0U, context.AppId);
    }

    [TestMethod]
    public void Activate_SetsCurrentAndAppId()
    {
        var context = new SessionContext();
        var session = Session(1162750);

        context.Activate(session);

        Assert.AreSame(session, context.Current);
        Assert.AreEqual(1162750U, context.AppId);
    }

    [TestMethod]
    public void Activate_ReplacesPreviousSession()
    {
        var context = new SessionContext();
        context.Activate(Session(1162750));
        var other = Session(294100);

        context.Activate(other);

        Assert.AreSame(other, context.Current);
        Assert.AreEqual(294100U, context.AppId);
    }

    [TestMethod]
    public void Update_SameId_ReplacesCurrent()
    {
        var context = new SessionContext();
        var original = Session(1162750);
        context.Activate(original);
        var edited = Session(1162750, original.Id);
        edited.CustomTags.Add("QoL");

        context.Update(edited);

        Assert.AreSame(edited, context.Current);
    }

    [TestMethod]
    public void Update_DifferentId_IsIgnored()
    {
        var context = new SessionContext();
        var original = Session(1162750);
        context.Activate(original);

        context.Update(Session(294100));

        Assert.AreSame(original, context.Current);
        Assert.AreEqual(1162750U, context.AppId);
    }

    [TestMethod]
    public void Update_WithoutActiveSession_IsIgnored()
    {
        var context = new SessionContext();

        context.Update(Session(1162750));

        Assert.IsNull(context.Current);
    }

    [TestMethod]
    public void Clear_DropsSessionAndResetsAppId()
    {
        var context = new SessionContext();
        context.Activate(Session(1162750));

        context.Clear();

        Assert.IsNull(context.Current);
        Assert.AreEqual(0U, context.AppId);
    }

    private static WorkshopSession Session(uint appId, string? id = null)
    {
        var session = new WorkshopSession { AppId = appId, Name = $"Game {appId}" };
        if (id is not null) session.Id = id;
        return session;
    }
}
