using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Tests;

[TestClass]
public static class TestAssemblySetup
{
    /// <summary>
    /// Info and above always reach disk now, so without this a test run appends
    /// its stub traffic to the real %LocalAppData% logs.
    /// </summary>
    [AssemblyInitialize]
    public static void Initialize(TestContext context) => LogService.Instance.DisableFileOutput();
}