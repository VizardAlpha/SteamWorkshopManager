using System.Collections.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Helpers;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ListReorderTests
{
    private static ObservableCollection<string> List() => ["a", "b", "c", "d"];

    [TestMethod]
    public void Move_DownwardsShiftsTheItemsInBetween()
    {
        var list = List();

        Assert.IsTrue(ListReorder.Move(list, list[0], list[2]));
        CollectionAssert.AreEqual(new[] { "b", "c", "a", "d" }, list);
    }

    [TestMethod]
    public void Move_UpwardsShiftsTheItemsInBetween()
    {
        var list = List();

        Assert.IsTrue(ListReorder.Move(list, list[3], list[1]));
        CollectionAssert.AreEqual(new[] { "a", "d", "b", "c" }, list);
    }

    [TestMethod]
    public void Move_OntoTheAdjacentItemSwapsThem()
    {
        var list = List();

        Assert.IsTrue(ListReorder.Move(list, list[0], list[1]));
        CollectionAssert.AreEqual(new[] { "b", "a", "c", "d" }, list);
    }

    [TestMethod]
    public void Move_OntoItselfIsANoOp()
    {
        var list = List();

        Assert.IsFalse(ListReorder.Move(list, list[1], list[1]));
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, list);
    }

    [TestMethod]
    public void Move_UnknownSourceIsANoOp()
    {
        var list = List();

        Assert.IsFalse(ListReorder.Move(list, "zz", list[0]));
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, list);
    }

    [TestMethod]
    public void Move_UnknownTargetIsANoOp()
    {
        var list = List();

        Assert.IsFalse(ListReorder.Move(list, list[0], "zz"));
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, list);
    }

    [TestMethod]
    public void Move_ComparesByReferenceNotValue()
    {
        // Two equal-but-distinct instances must still count as different rows.
        var first = new string(['x']);
        var second = new string(['x']);
        var list = new ObservableCollection<string> { first, second, "y" };

        Assert.IsTrue(ListReorder.Move(list, first, "y"));
        CollectionAssert.AreEqual(new[] { second, "y", first }, list);
    }
}