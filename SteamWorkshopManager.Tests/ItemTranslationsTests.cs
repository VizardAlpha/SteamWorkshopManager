using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ItemTranslationsTests
{
    private static readonly ItemText Base = new("My mod", "English description");

    [TestMethod]
    public void IsTranslated_SameAsBase_False() =>
        Assert.IsFalse(ItemTranslations.IsTranslated(Base, new ItemText("My mod", "English description")));

    [TestMethod]
    [DataRow("Mon mod", "English description")]
    [DataRow("My mod", "Description française")]
    public void IsTranslated_AnyFieldDiffers_True(string title, string description) =>
        Assert.IsTrue(ItemTranslations.IsTranslated(Base, new ItemText(title, description)));

    [TestMethod]
    public void IsTranslated_BlankTranslation_True() =>
        Assert.IsTrue(ItemTranslations.IsTranslated(Base, new ItemText("", "")));

    [TestMethod]
    public void ResolveForSubmit_EmptyFieldsFallBackToBase()
    {
        var resolved = ItemTranslations.ResolveForSubmit(new ItemText("Mon mod", "  "), Base);
        Assert.AreEqual(new ItemText("Mon mod", "English description"), resolved);
    }

    [TestMethod]
    public void PlanChange_UnchangedTranslation_Null()
    {
        var steam = new ItemText("Mon mod", "Description");
        Assert.IsNull(ItemTranslations.PlanChange("french", steam, steam, Base));
    }

    [TestMethod]
    public void PlanChange_EditedTranslation_Upsert()
    {
        var change = ItemTranslations.PlanChange("french", new ItemText("Mon mod", "Ancienne"),
            new ItemText("Mon mod", "Nouvelle"), Base);
        Assert.IsNotNull(change);
        Assert.AreEqual(new ItemText("Mon mod", "Nouvelle"), change.Text);
    }

    [TestMethod]
    public void PlanChange_NewLanguageWithTitleOnly_KeepsBaseDescription()
    {
        var change = ItemTranslations.PlanChange("german", null, new ItemText("Mein Mod", ""), Base);
        Assert.IsNotNull(change);
        Assert.AreEqual(new ItemText("Mein Mod", "English description"), change.Text);
    }

    [TestMethod]
    public void PlanChange_NewLanguageLeftEmpty_Null() =>
        Assert.IsNull(ItemTranslations.PlanChange("german", null, new ItemText("", ""), Base));

    [TestMethod]
    public void PlanChange_ClearedTranslation_SendsBaseNeverEmpty()
    {
        var change = ItemTranslations.PlanChange("french", new ItemText("Mon mod", "Desc"), new ItemText("", " "), Base);
        Assert.IsNotNull(change);
        Assert.AreEqual(Base, change.Text);
    }

    [TestMethod]
    public void PlanChange_BlankOnSteamUntouched_Null() =>
        Assert.IsNull(ItemTranslations.PlanChange("french", new ItemText("", ""), new ItemText("", ""), Base));

    [TestMethod]
    public void SteamLanguages_TranslatableExcludesBase()
    {
        Assert.AreEqual(SteamLanguages.All.Count - 1, SteamLanguages.Translatable.Count);
        Assert.IsFalse(SteamLanguages.Translatable.Contains(SteamLanguages.Base));
        Assert.AreEqual("english", SteamLanguages.Base.ApiCode);
    }

    [TestMethod]
    public void SteamLanguages_CodesAreUnique() =>
        Assert.AreEqual(SteamLanguages.All.Count, SteamLanguages.All.Select(l => l.ApiCode).Distinct().Count());
}
