using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Platforms;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ModPlatformsTests
{
    private sealed class FakePlatform(WorkshopPlatform platform) : IModPlatform
    {
        public WorkshopPlatform Platform => platform;
        public bool IsReady => true;
        public string NotReadyMessageKey => "";
        public ulong AccountId => 1;
        public Task<List<WorkshopItem>> GetMyModsAsync() => Task.FromResult(new List<WorkshopItem>());
        public Task<WorkshopItem?> GetModAsync(ulong id) => Task.FromResult<WorkshopItem?>(null);
        public Task<bool> SetVisibilityAsync(ulong id, VisibilityType visibility) => Task.FromResult(true);
        public Task<bool> DeleteModAsync(ulong id) => Task.FromResult(true);
    }

    [TestMethod]
    public void Current_FollowsActiveSessionPlatform()
    {
        var steam = new FakePlatform(WorkshopPlatform.Steam);
        var context = new SessionContext();
        var platforms = new ModPlatforms([steam], context);

        Assert.IsNull(platforms.Current);

        context.Activate(new SteamSession { AppId = 1 });
        Assert.AreSame(steam, platforms.Current);

        // No mod.io implementation registered yet.
        context.Activate(new ModioSession { GameId = 1 });
        Assert.IsNull(platforms.Current);
    }
}
