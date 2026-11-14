using WorkshopManager.Core.Workshop;
using WorkshopManager.Models;

namespace WorkshopManager.Tests;

[TestClass]
public class ContentFolderPatternTests
{
    private static WorkshopItem Mod(ulong id, string title) => new() { Id = id, Title = title };

    private static string NewRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "swm-pattern-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MakeFolder(string root, params string[] segments)
    {
        var path = Path.Combine([root, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }

    [TestMethod]
    [DataRow("Better Storage (v2)", "betterstoragev2")]
    [DataRow("better_storage-V2", "betterstoragev2")]
    [DataRow("  Été  ", "été")]
    [DataRow("!!!", "")]
    public void Normalize_KeepsLettersAndDigitsLowerCased(string value, string expected) =>
        Assert.AreEqual(expected, ContentFolderPattern.Normalize(value));

    [TestMethod]
    public void IsValid_NeedsAnAbsolutePathWithAPlaceholderBelowTheRoot()
    {
        var root = NewRoot();
        try
        {
            Assert.IsTrue(ContentFolderPattern.IsValid(Path.Combine(root, "{name}", "Pak")));
            Assert.IsTrue(ContentFolderPattern.IsValid(Path.Combine(root, "{id}")));
            Assert.IsFalse(ContentFolderPattern.IsValid(Path.Combine(root, "Pak")));
            Assert.IsFalse(ContentFolderPattern.IsValid(Path.Combine("Mods", "{name}")));
            Assert.IsFalse(ContentFolderPattern.IsValid(""));
            Assert.IsFalse(ContentFolderPattern.IsValid(null));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Resolve_MatchesFolderNamesWithoutCaseSpacesOrPunctuation()
    {
        var root = NewRoot();
        try
        {
            var storage = MakeFolder(root, "BetterStorage_v2", "Pak");
            var roads = MakeFolder(root, "stone roads", "Pak");
            MakeFolder(root, "NoPakHere");

            var found = ContentFolderPattern.Resolve(
                Path.Combine(root, "{name}", "Pak"),
                [Mod(1, "Better Storage (v2)"), Mod(2, "Stone Roads"), Mod(3, "No Pak Here"), Mod(4, "Missing")]);

            Assert.AreEqual(storage, found[1]);
            Assert.AreEqual(roads, found[2]);
            Assert.IsFalse(found.ContainsKey(3));
            Assert.IsFalse(found.ContainsKey(4));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Resolve_FindsTheFixedSubfolderWhateverItsCase()
    {
        var root = NewRoot();
        try
        {
            var expected = MakeFolder(root, "Alpha", "pak");

            var found = ContentFolderPattern.Resolve(Path.Combine(root, "{name}", "Pak"), [Mod(1, "Alpha")]);

            // Windows keeps the pattern's spelling, case-sensitive systems give the real one.
            Assert.IsTrue(string.Equals(expected, found[1], StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(Directory.Exists(found[1]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Resolve_ById_AndWithTextAroundThePlaceholder()
    {
        var root = NewRoot();
        try
        {
            var byId = MakeFolder(root, "42");
            var prefixed = MakeFolder(root, "Mod_Alpha");

            Assert.AreEqual(byId, ContentFolderPattern.Resolve(Path.Combine(root, "{id}"), [Mod(42, "Anything")])[42]);
            Assert.AreEqual(prefixed, ContentFolderPattern.Resolve(Path.Combine(root, "Mod_{name}"), [Mod(1, "alpha")])[1]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Resolve_AcceptsEitherSeparatorInThePattern()
    {
        var root = NewRoot();
        try
        {
            var expected = MakeFolder(root, "Alpha", "Pak");
            var forward = root.Replace('\\', '/') + "/{name}/Pak";

            Assert.AreEqual(expected, ContentFolderPattern.Resolve(forward, [Mod(1, "Alpha")])[1]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Resolve_TwoFoldersForOneMod_MatchesNeither()
    {
        var root = NewRoot();
        try
        {
            MakeFolder(root, "Better Storage");
            MakeFolder(root, "Better_Storage");

            Assert.AreEqual(0, ContentFolderPattern.Resolve(Path.Combine(root, "{name}"), [Mod(1, "BetterStorage")]).Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Resolve_MissingRootOrInvalidPattern_FindsNothing()
    {
        var root = NewRoot();
        try
        {
            var mods = new[] { Mod(1, "Alpha") };

            Assert.AreEqual(0, ContentFolderPattern.Resolve(Path.Combine(root, "gone", "{name}"), mods).Count);
            Assert.AreEqual(0, ContentFolderPattern.Resolve("not a pattern", mods).Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Plan_OnlyLooksForModsWithoutAUsableFolder_AndMarksWhatThePatternFound()
    {
        var root = NewRoot();
        try
        {
            var known = MakeFolder(root, "known");
            File.WriteAllText(Path.Combine(known, "a.pak"), "x");
            var guessed = MakeFolder(root, "mods", "Beta", "Pak");
            File.WriteAllText(Path.Combine(guessed, "b.pak"), "12345");
            MakeFolder(root, "mods", "Alpha", "Pak");

            var alpha = Mod(1, "Alpha");
            var beta = Mod(2, "Beta");
            var gamma = Mod(3, "Gamma");
            var stored = new Dictionary<ulong, ItemFileInfo?>
            {
                [1] = new ItemFileInfo { Path = known },
                [2] = null,
                [3] = null,
            };

            var plan = BulkUpdatePlanner.Plan([alpha, beta, gamma], stored, Path.Combine(root, "mods", "{name}", "Pak"));

            // Alpha keeps its remembered folder even though the pattern has one for it too.
            Assert.AreEqual(known, plan[0].Folder);
            Assert.IsFalse(plan[0].FromPattern);
            Assert.AreEqual(guessed, plan[1].Folder);
            Assert.IsTrue(plan[1].FromPattern);
            Assert.AreEqual(BulkUpdateState.Ready, plan[1].State);
            Assert.AreEqual(5, plan[1].SizeBytes);
            Assert.AreEqual(BulkUpdateState.NoFolder, plan[2].State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
