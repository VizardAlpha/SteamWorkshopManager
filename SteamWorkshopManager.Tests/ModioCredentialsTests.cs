using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Services.Platforms.Modio;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ModioCredentialsTests
{
    [TestMethod]
    [DataRow("https://u-123.modapi.io/v1", "https://u-123.modapi.io/v1/")]
    [DataRow(" https://U-123.ModAPI.io/v1/ ", "https://u-123.modapi.io/v1/")]
    [DataRow("https://g-5.modapi.io/v1/", "https://g-5.modapi.io/v1/")]
    [DataRow("https://u-9.test.mod.io/v1", "https://u-9.test.mod.io/v1/")]
    public void BaseUrl_AcceptsModioHosts(string input, string expected)
    {
        Assert.IsTrue(ModioCredentials.TryParseBaseUrl(input, out var url));
        Assert.AreEqual(expected, url.ToString());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("u-123.modapi.io/v1")]
    [DataRow("http://u-123.modapi.io/v1")]
    [DataRow("https://u-123.modapi.io")]
    [DataRow("https://u-123.modapi.io/v2")]
    [DataRow("https://u-123.modapi.io:8443/v1")]
    [DataRow("https://user:pw@u-123.modapi.io/v1")]
    [DataRow("https://u-123.modapi.io/v1?x=1")]
    [DataRow("https://modapi.io.evil.com/v1")]
    [DataRow("https://evilmodapi.io/v1")]
    [DataRow("https://api.mod.io/v1")]
    public void BaseUrl_RejectsAnythingElse(string input)
    {
        Assert.IsFalse(ModioCredentials.TryParseBaseUrl(input, out _));
    }

    [TestMethod]
    public void ToString_HidesToken()
    {
        var auth = new ModioCredentials(new System.Uri("https://u-1.modapi.io/v1/"), "secret-token");
        Assert.IsFalse(auth.ToString().Contains("secret-token"));
    }

    [TestMethod]
    [DataRow(".DS_Store")]
    [DataRow("textures/.ds_store")]
    [DataRow("textures/hi/._a.png")]
    [DataRow("__MACOSX/foo.txt")]
    [DataRow("nested/__MACOSX/bar.txt")]
    [DataRow("scripts/THUMBS.DB")]
    [DataRow("desktop.ini")]
    public void Archive_SkipsOsJunk(string entry) => Assert.IsTrue(ModioContentArchive.IsOsJunk(entry));

    [TestMethod]
    [DataRow("mod.json")]
    [DataRow("textures/hi/a.png")]
    [DataRow("_config.cfg")]
    [DataRow("my__MACOSX_folder/data.bin")]
    [DataRow("notThumbs.db.txt")]
    public void Archive_KeepsRealContent(string entry) => Assert.IsFalse(ModioContentArchive.IsOsJunk(entry));

    [TestMethod]
    public void Archive_UsesForwardSlashesAndDropsJunk()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swm-archive-{System.Guid.NewGuid():N}");
        var zip = root + ".zip";
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "sub", "a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "Thumbs.db"), "x");

            ModioContentArchive.Create(root, zip);

            using var archive = ZipFile.OpenRead(zip);
            CollectionAssert.AreEqual(new[] { "sub/a.txt" }, archive.Entries.Select(e => e.FullName).ToArray());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (File.Exists(zip)) File.Delete(zip);
        }
    }
}
