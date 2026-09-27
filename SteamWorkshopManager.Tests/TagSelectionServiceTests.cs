using System.Collections.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class TagSelectionServiceTests
{
    [TestMethod]
    public void CollectSelectedNames_ReturnsCategoryThenCustomSelections()
    {
        var categories = Categories(
            ("Type", [("Mod", true), ("Map", false)]),
            ("Era", [("Medieval", true)]));
        var custom = new[] { new WorkshopTag("QoL", isSelected: true), new WorkshopTag("Unused") };

        var names = TagSelectionService.CollectSelectedNames(categories, custom);

        Assert.AreSequenceEqual(new[] { "Mod", "Medieval", "QoL" }, names);
    }

    [TestMethod]
    public void CollectSelectedNames_DedupesCaseInsensitively_KeepingFirstSpelling()
    {
        var categories = Categories(("Type", [("Mod", true)]));
        var custom = new[] { new WorkshopTag("mod", isSelected: true), new WorkshopTag("MOD", isSelected: true) };

        var names = TagSelectionService.CollectSelectedNames(categories, custom);

        Assert.AreSequenceEqual(new[] { "Mod" }, names);
    }

    [TestMethod]
    public void CollectSelectedNames_NothingSelected_ReturnsEmpty()
    {
        var categories = Categories(("Type", [("Mod", false)]));

        var names = TagSelectionService.CollectSelectedNames(categories, [new WorkshopTag("QoL")]);

        Assert.AreEqual(0, names.Count);
    }

    [TestMethod]
    public void RestoreFromDraft_RestoresSelectionCaseInsensitively()
    {
        var categories = Categories(("Type", [("Mod", true), ("Map", false)]));
        var custom = new ObservableCollection<WorkshopTag>();

        TagSelectionService.RestoreFromDraft(Draft(selected: ["map"]), categories, custom);

        var tags = categories[0].Tags;
        Assert.IsFalse(tags[0].IsSelected);
        Assert.IsTrue(tags[1].IsSelected);
        Assert.AreEqual(0, custom.Count);
    }

    [TestMethod]
    public void RestoreFromDraft_MergesDraftCustomTagsWithoutDuplicates()
    {
        var categories = Categories(("Type", [("Mod", false)]));
        var custom = new ObservableCollection<WorkshopTag> { new("QoL") };

        TagSelectionService.RestoreFromDraft(
            Draft(customTags: ["qol", "Balance"], selected: ["Balance"]), categories, custom);

        Assert.AreSequenceEqual(new[] { "QoL", "Balance" }, custom.Select(t => t.Name).ToList());
        Assert.IsFalse(custom[0].IsSelected);
        Assert.IsTrue(custom[1].IsSelected);
    }

    [TestMethod]
    public void RestoreFromDraft_UnknownSelectedName_IsPromotedToSelectedCustomTag()
    {
        var categories = Categories(("Type", [("Mod", false)]));
        var custom = new ObservableCollection<WorkshopTag>();

        TagSelectionService.RestoreFromDraft(Draft(selected: ["Mod", "Retired Category Tag"]), categories, custom);

        Assert.IsTrue(categories[0].Tags[0].IsSelected);
        Assert.AreEqual(1, custom.Count);
        Assert.AreEqual("Retired Category Tag", custom[0].Name);
        Assert.IsTrue(custom[0].IsSelected);
    }

    [TestMethod]
    public void RestoreFromDraft_NullLists_ClearsSelection()
    {
        var categories = Categories(("Type", [("Mod", true)]));
        var custom = new ObservableCollection<WorkshopTag> { new("QoL", isSelected: true) };

        TagSelectionService.RestoreFromDraft(Draft(), categories, custom);

        Assert.IsFalse(categories[0].Tags[0].IsSelected);
        Assert.IsFalse(custom[0].IsSelected);
        Assert.AreEqual(1, custom.Count);
    }

    private static ObservableCollection<TagCategory> Categories(
        params (string Name, (string Tag, bool Selected)[] Tags)[] groups) =>
        new(groups.Select(g => new TagCategory
        {
            Name = g.Name,
            Tags = new ObservableCollection<WorkshopTag>(g.Tags.Select(t => new WorkshopTag(t.Tag, t.Selected))),
        }));

    private static CreateDraft Draft(List<string>? customTags = null, List<string>? selected = null) =>
        new(
            TempId: "draft",
            AppId: 1162750,
            Title: "My mod",
            Description: string.Empty,
            ContentFolderPath: null,
            PreviewImagePath: null,
            Visibility: VisibilityType.Public,
            InitialChangelog: string.Empty,
            TargetAllVersions: true,
            BranchMin: null,
            BranchMax: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            CustomTags: customTags,
            SelectedTags: selected);
}
