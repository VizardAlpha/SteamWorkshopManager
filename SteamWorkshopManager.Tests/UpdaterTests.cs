using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkshopManager.Helpers;
using WorkshopManager.Models;
using WorkshopManager.Services.Core;
using WorkshopManager.Services.Notifications;
using WorkshopManager.Services.Steam;

namespace WorkshopManager.Tests;

[TestClass]
public class UpdaterTests
{
    private const string Repo = "https://github.com/VizardAlpha/WorkshopManager";
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    [DataRow($"{Hash}  WorkshopManager-windows.zip")]
    [DataRow($"{Hash} *WorkshopManager-windows.zip")]
    [DataRow($"{Hash}  ./WorkshopManager-windows.zip")]
    public void ParseChecksum_FindsHashInSha256sumFormats(string line)
    {
        var sums = $"{new string('f', 64)}  WorkshopManager-linux.zip\n{line}\r\n";

        Assert.AreEqual(Hash, AppUpdater.ParseChecksum(sums, "WorkshopManager-windows.zip"));
    }

    [TestMethod]
    public void ParseChecksum_MissingOrMalformed_ReturnsNull()
    {
        Assert.IsNull(AppUpdater.ParseChecksum($"{Hash}  other.zip", "WorkshopManager-windows.zip"));
        Assert.IsNull(AppUpdater.ParseChecksum("abc  WorkshopManager-windows.zip", "WorkshopManager-windows.zip"));
    }

    [TestMethod]
    public void FindPayloadRoot_HandlesFlatAndDistPrefixedZips()
    {
        var root = Path.Combine(Path.GetTempPath(), "swm-payload-" + Guid.NewGuid().ToString("N"));
        try
        {
            var flat = Directory.CreateDirectory(Path.Combine(root, "flat")).FullName;
            File.WriteAllText(Path.Combine(flat, "App.exe"), "");
            Assert.AreEqual(flat, AppUpdater.FindPayloadRoot(flat, "App.exe"));

            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            var dist = Directory.CreateDirectory(Path.Combine(nested, "dist")).FullName;
            File.WriteAllText(Path.Combine(dist, "App.exe"), "");
            Assert.AreEqual(dist, AppUpdater.FindPayloadRoot(nested, "App.exe"));

            var empty = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
            Assert.IsNull(AppUpdater.FindPayloadRoot(empty, "App.exe"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void TrustedUrls_OnlyAcceptThisRepo()
    {
        Assert.IsTrue(UpdateCheckerService.IsTrustedReleasePageUrl($"{Repo}/releases/tag/v1.8.10"));
        Assert.IsTrue(UpdateCheckerService.IsTrustedAssetUrl($"{Repo}/releases/download/v1.8.10/SHA256SUMS.txt"));

        Assert.IsFalse(UpdateCheckerService.IsTrustedReleasePageUrl("file:///C:/Windows/System32/calc.exe"));
        Assert.IsFalse(UpdateCheckerService.IsTrustedReleasePageUrl("https://github.com/evil/WorkshopManager/releases/x"));
        Assert.IsFalse(UpdateCheckerService.IsTrustedAssetUrl("https://github.com/VizardAlpha/WorkshopManager.evil.com/releases/download/x"));
        Assert.IsFalse(UpdateCheckerService.IsTrustedAssetUrl(null));
    }

    [TestMethod]
    public void ToUpdateInfo_PicksPlatformPackageAndChecksums()
    {
        var package = UpdateCheckerService.PlatformPackageName!;
        var release = new GitHubRelease
        {
            TagName = "v9.9.9",
            HtmlUrl = $"{Repo}/releases/tag/v9.9.9",
            Assets =
            [
                new GitHubReleaseAsset { Name = package, BrowserDownloadUrl = $"{Repo}/releases/download/v9.9.9/{package}", Size = 42 },
                new GitHubReleaseAsset { Name = "SHA256SUMS.txt", BrowserDownloadUrl = $"{Repo}/releases/download/v9.9.9/SHA256SUMS.txt" },
            ],
        };

        var info = UpdateCheckerService.ToUpdateInfo(release);

        Assert.IsTrue(info.CanInstallInApp);
        Assert.AreEqual(package, info.PackageName);
        Assert.AreEqual(42, info.PackageSize);
    }

    [TestMethod]
    public void ToUpdateInfo_WithoutChecksums_FallsBackToManualDownload()
    {
        var package = UpdateCheckerService.PlatformPackageName!;
        var release = new GitHubRelease
        {
            TagName = "v9.9.9",
            HtmlUrl = "https://evil.example/phish",
            Assets = [new GitHubReleaseAsset { Name = package, BrowserDownloadUrl = $"{Repo}/releases/download/v9.9.9/{package}" }],
        };

        var info = UpdateCheckerService.ToUpdateInfo(release);

        Assert.IsFalse(info.CanInstallInApp);
        Assert.AreEqual($"{Repo}/releases", info.ReleaseUrl);
    }

    [TestMethod]
    [DataRow("1.8.10-beta.1", "1.8.10")]
    [DataRow("1.8.10-alpha.1", "1.8.10-beta.1")]
    [DataRow("1.8.10-beta.2", "1.8.10-beta.10")]
    [DataRow("1.8.10-beta2", "1.8.10-beta10")]
    [DataRow("1.8.9", "1.8.10-beta.1")]
    [DataRow("v1.8.10", "1.9.0-alpha")]
    [DataRow("1.9", "1.9.1")]
    public void SemVersion_OrdersPreReleasesBeforeFinal(string lower, string higher)
    {
        var a = SemVersion.Parse(lower)!;
        var b = SemVersion.Parse(higher)!;
        Assert.IsTrue(a < b, $"{lower} should be lower than {higher}");
        Assert.IsTrue(b > a);
    }

    [TestMethod]
    public void SemVersion_ParsesAndNormalizes()
    {
        Assert.AreEqual(SemVersion.Parse("1.8.10"), SemVersion.Parse("v1.8.10+abc123"));
        Assert.AreEqual(0, SemVersion.Parse("1.8")!.CompareTo(SemVersion.Parse("1.8.0")));
        Assert.IsTrue(SemVersion.Parse("1.8.10-beta.1")!.IsPreRelease);
        Assert.IsNull(SemVersion.Parse("not-a-version"));
        Assert.IsNull(SemVersion.Parse("1.8.10-"));
    }

    [TestMethod]
    public void PickLatest_StableChannelIgnoresBetasEvenWhenNotFlagged()
    {
        var releases = new[]
        {
            new GitHubRelease { TagName = "v1.8.9" },
            new GitHubRelease { TagName = "v1.8.10-beta.1", Prerelease = true },
            new GitHubRelease { TagName = "v1.9.0-alpha.1" }, // suffix but not flagged on GitHub
        };

        Assert.AreEqual("v1.8.9", UpdateCheckerService.PickLatest(releases, includePrereleases: false)!.Value.Release.TagName);
        Assert.AreEqual("v1.9.0-alpha.1", UpdateCheckerService.PickLatest(releases, includePrereleases: true)!.Value.Release.TagName);
    }

    [TestMethod]
    public void PickLatest_FinalReleaseBeatsItsBetas()
    {
        var releases = new[]
        {
            new GitHubRelease { TagName = "v1.8.10-beta.3", Prerelease = true },
            new GitHubRelease { TagName = "v1.8.10" },
        };

        Assert.AreEqual("v1.8.10", UpdateCheckerService.PickLatest(releases, includePrereleases: true)!.Value.Release.TagName);
    }

    [TestMethod]
    public void MarkdownToBbCode_ConvertsReleaseNotesSubset()
    {
        const string md = """
            ## v1.8.10

            ### Bug Fixes
            - Fixed **upload** timeout, see [issue](https://github.com/x/y/issues/1)
            * Kept snake_case_names and `code`
            ![shot](https://example.com/a.png)
            Plain *italic* line
            """;

        var bb = MarkdownToBbCode.Convert(md);

        StringAssert.Contains(bb, "[h2]v1.8.10[/h2]");
        StringAssert.Contains(bb, "[h3]Bug Fixes[/h3]");
        StringAssert.Contains(bb, "[list]\n[*]Fixed [b]upload[/b] timeout, see [url=https://github.com/x/y/issues/1]issue[/url]");
        StringAssert.Contains(bb, "[*]Kept snake_case_names and code\n[/list]");
        StringAssert.Contains(bb, "Plain [i]italic[/i] line");
        Assert.IsFalse(bb.Contains("example.com"), "Images must be dropped");
    }

    [TestMethod]
    public void MarkdownToBbCode_EmptyInput_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, MarkdownToBbCode.Convert(null));
        Assert.AreEqual(string.Empty, MarkdownToBbCode.Convert("  \n "));
    }

    [TestMethod]
    [DataRow("0f8fad5bd9cb469fa16570867728950e", true)]
    [DataRow("0f8fad5b-d9cb-469f-a165-70867728950e", true)]
    [DataRow("..\\..\\Documents", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void DraftTempId_OnlyGuidsAreValid(string? id, bool expected) =>
        Assert.AreEqual(expected, DraftService.IsValidTempId(id));

    [TestMethod]
    public void SteamCredentialStore_RoundTripsAndDeletesWhenEmpty()
    {
        var previous = SteamCredentialStore.FilePath;
        var path = Path.Combine(Path.GetTempPath(), "swm-creds-" + Guid.NewGuid().ToString("N"), "credentials.bin");
        SteamCredentialStore.FilePath = path;
        try
        {
            var creds = new SteamCredentials("refresh-token", "access-token", "account", 76561197960287930UL);
            SteamCredentialStore.Save(creds);

            Assert.AreEqual(creds, SteamCredentialStore.Load());
            if (OperatingSystem.IsWindows())
                Assert.IsFalse(File.ReadAllText(path).Contains("refresh-token"), "Token must not be stored in clear on Windows");

            SteamCredentialStore.Save(new SteamCredentials(null, null, null, 0));
            Assert.IsFalse(File.Exists(path));
            Assert.IsNull(SteamCredentialStore.Load());
        }
        finally
        {
            SteamCredentialStore.FilePath = previous;
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static (string Source, string Target, string Root) UpdateFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "swm-install-" + Guid.NewGuid().ToString("N"));
        var source = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(root, "install")).FullName;
        return (source, target, root);
    }

    [TestMethod]
    public void InstallPayload_ReplacesAddsAndDropsFilesTheNewVersionNoLongerShips()
    {
        var (source, target, root) = UpdateFolders();
        try
        {
            File.WriteAllText(Path.Combine(source, "app.dll"), "new");
            Directory.CreateDirectory(Path.Combine(source, "sub"));
            File.WriteAllText(Path.Combine(source, "sub", "added.dll"), "added");

            File.WriteAllText(Path.Combine(target, "app.dll"), "old");
            File.WriteAllText(Path.Combine(target, "gone.dll"), "shipped by the previous update");
            File.WriteAllText(Path.Combine(target, "steam_appid.txt"), "written by the app, never shipped");
            File.WriteAllLines(Path.Combine(target, AppUpdater.ManifestName), ["app.dll", "gone.dll", @"..\outside.txt"]);
            File.WriteAllText(Path.Combine(root, "outside.txt"), "must survive");

            AppUpdater.InstallPayload(source, target, attempts: 1);

            Assert.AreEqual("new", File.ReadAllText(Path.Combine(target, "app.dll")));
            Assert.AreEqual("added", File.ReadAllText(Path.Combine(target, "sub", "added.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(target, "gone.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "steam_appid.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "outside.txt")), "A manifest entry must not reach outside the install folder");
            Assert.IsFalse(Directory.Exists(Path.Combine(target, ".update-backup")));
            CollectionAssert.AreEquivalent(
                new[] { "app.dll", Path.Combine("sub", "added.dll") },
                File.ReadAllLines(Path.Combine(target, AppUpdater.ManifestName)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void InstallPayload_FailureMidway_RestoresThePreviousFiles()
    {
        var (source, target, root) = UpdateFolders();
        try
        {
            File.WriteAllText(Path.Combine(source, "a.dll"), "new a");
            File.WriteAllText(Path.Combine(source, "b.dll"), "new b");
            File.WriteAllText(Path.Combine(source, "c.dll"), "new c");

            File.WriteAllText(Path.Combine(target, "a.dll"), "old a");
            // A folder where a file is expected makes the copy of b.dll fail.
            Directory.CreateDirectory(Path.Combine(target, "b.dll"));

            try
            {
                AppUpdater.InstallPayload(source, target, attempts: 1);
                Assert.Fail("The copy over a folder should have failed");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            Assert.AreEqual("old a", File.ReadAllText(Path.Combine(target, "a.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(target, "c.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(target, AppUpdater.ManifestName)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void LegacyBinaries_AreRemovedUnlessStillRunningUnderTheOldName()
    {
        var (_, target, root) = UpdateFolders();
        try
        {
            var oldExe = Path.Combine(target, "SteamWorkshopManager.exe");
            var oldDll = Path.Combine(target, "SteamWorkshopManager.dll");
            var other = Path.Combine(target, "SteamKit2.dll");
            foreach (var file in new[] { oldExe, oldDll, other }) File.WriteAllText(file, "x");

            LegacyBinaries.Remove(target, oldExe);
            Assert.IsTrue(File.Exists(oldDll));

            LegacyBinaries.Remove(target, Path.Combine(target, "WorkshopManager.exe"));
            Assert.IsFalse(File.Exists(oldExe));
            Assert.IsFalse(File.Exists(oldDll));
            Assert.IsTrue(File.Exists(other));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
