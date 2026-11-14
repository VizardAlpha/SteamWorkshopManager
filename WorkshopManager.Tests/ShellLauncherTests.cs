using WorkshopManager.Services.UI;

namespace WorkshopManager.Tests;

[TestClass]
public class ShellLauncherTests
{
    [TestMethod]
    [DataRow("https://steamcommunity.com/sharedfiles/filedetails/?id=1")]
    [DataRow("http://example.com")]
    public void IsWebUrl_AcceptsHttpAndHttps(string url) => Assert.IsTrue(ShellLauncher.IsWebUrl(url));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("example.com")]
    [DataRow("file:///C:/Windows/System32/calc.exe")]
    [DataRow(@"C:\Windows\System32\calc.exe")]
    [DataRow("steam://run/480")]
    [DataRow("javascript:alert(1)")]
    public void IsWebUrl_RefusesEverythingElse(string? url) => Assert.IsFalse(ShellLauncher.IsWebUrl(url));

    [TestMethod]
    public void OpenUrl_RefusesANonWebUrlWithoutLaunchingAnything() =>
        Assert.IsFalse(new ShellLauncher().OpenUrl(@"C:\Windows\System32\calc.exe"));

    [TestMethod]
    public void OpenFolder_MissingFolder_ReturnsFalse() =>
        Assert.IsFalse(new ShellLauncher().OpenFolder(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
}
