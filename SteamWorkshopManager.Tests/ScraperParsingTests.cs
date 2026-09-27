using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Services.Workshop;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ScraperParsingTests
{
    private const string DeclaredTagsEntry = """
        {"declaredTags":{
          "readytouse_tags":[
            {"name":"Type","htmlelement":"select","tags":[
              {"name":"mod","display_name":"Mod","admin_only":false},
              {"name":"secret","display_name":"Secret","admin_only":true},
              {"name":"mod","display_name":"Mod","admin_only":false}]},
            {"name":"","htmlelement":null,"tags":[
              {"name":"misc","display_name":null,"admin_only":false}]}],
          "mtx_tags":[
            {"name":"Legacy","htmlelement":null,"tags":[{"name":"x","display_name":"X","admin_only":false}]}]
        }}
        """;

    // The page embeds loaderData as an array of JSON-encoded strings.
    private static string WorkshopPage(params string[] loaderEntries) =>
        $"<html><script>window.SSR.loaderData = {JsonSerializer.Serialize(loaderEntries)};</script></html>";

    [TestMethod]
    public void ParseWorkshopPage_ReadsReadyToUseGroups()
    {
        var result = WorkshopTagsService.ParseWorkshopPage(WorkshopPage("not json", DeclaredTagsEntry));

        CollectionAssert.AreEqual(new[] { "Mod" }, result.TagsByCategory["Type"], "admin-only and duplicate tags are dropped");
        CollectionAssert.AreEqual(new[] { "misc" }, result.TagsByCategory["Tags"], "untitled group and missing display name fall back");
        CollectionAssert.AreEqual(new[] { "Type" }, result.DropdownCategories);
        Assert.IsFalse(result.TagsByCategory.ContainsKey("Legacy"));
    }

    [TestMethod]
    public void ParseWorkshopPage_FallsBackToMtxTags()
    {
        const string entry = """{"declaredTags":{"readytouse_tags":[],"mtx_tags":[{"name":"Legacy","htmlelement":null,"tags":[{"name":"x","display_name":"X","admin_only":false}]}]}}""";

        var result = WorkshopTagsService.ParseWorkshopPage(WorkshopPage(entry));

        CollectionAssert.AreEqual(new[] { "X" }, result.TagsByCategory["Legacy"]);
        Assert.AreEqual(0, result.DropdownCategories.Count);
    }

    [TestMethod]
    [DataRow("<html>no ssr payload</html>")]
    [DataRow("<script>window.SSR.loaderData = [\"{}\"];</script>")]
    public void ParseWorkshopPage_UnexpectedPage_ReturnsEmpty(string html)
    {
        var result = WorkshopTagsService.ParseWorkshopPage(html);

        Assert.AreEqual(0, result.TagsByCategory.Count);
        Assert.AreEqual(0, result.DropdownCategories.Count);
    }

    [TestMethod]
    public void ChangelogParse_ReadsEmbeddedEntries()
    {
        const string html = """
            <script>
            changeLogs[0] = {"timestamp":1700000000,"change_description":"See https:\/\/example.com","manifest_id":"1234567890","language":0,"saved_snapshot":null,"snapshot_gamebranch_min":"public","snapshot_gamebranch_max":"","accountid":42};
            changeLogs[1] = {"timestamp":1690000000,"change_description":"","manifest_id":"","language":0,"accountid":42};
            changeLogs[2] = {"timestamp":broken,"accountid":1};
            </script>
            """;

        var entries = ChangelogScraperService.Parse(html);

        Assert.AreEqual(2, entries.Count, "the malformed entry is skipped");
        Assert.AreEqual(1700000000, entries[0].Timestamp);
        Assert.AreEqual("See https://example.com", entries[0].ChangeDescription);
        Assert.AreEqual("1234567890", entries[0].ManifestId);
        Assert.IsTrue(entries[0].HasBranches);
        Assert.IsFalse(entries[1].HasManifestId);
    }

    [TestMethod]
    public void ChangelogParse_PageWithoutEntries_ReturnsEmpty() =>
        Assert.AreEqual(0, ChangelogScraperService.Parse("<html>Please sign in</html>").Count);
}
