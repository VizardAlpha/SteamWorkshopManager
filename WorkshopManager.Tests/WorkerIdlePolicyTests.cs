using WorkshopManager.Models;
using WorkshopManager.Services.Session;

namespace WorkshopManager.Tests;

[TestClass]
public class WorkerIdlePolicyTests
{
    [TestMethod]
    public void StayConnected_NeverGoesOnStandby() =>
        Assert.IsNull(WorkerIdlePolicy.DelayFor(SteamIdleMode.StayConnected, 60));

    [TestMethod]
    public void AfterEachAction_WaitsAShortGraceWhateverTheDelaySetting()
    {
        var delay = WorkerIdlePolicy.DelayFor(SteamIdleMode.AfterEachAction, 900);

        Assert.IsNotNull(delay);
        Assert.IsTrue(delay.Value < TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    [DataRow(120, 120)]
    [DataRow(0, WorkerIdlePolicy.MinSeconds)]
    [DataRow(999999, WorkerIdlePolicy.MaxSeconds)]
    public void AfterInactivity_UsesTheUserDelayWithinBounds(int seconds, int expected) =>
        Assert.AreEqual(TimeSpan.FromSeconds(expected), WorkerIdlePolicy.DelayFor(SteamIdleMode.AfterInactivity, seconds));
}
