using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class BbCodeParserTests
{
    private static BbCodeElement Element(BbCodeNode node)
    {
        Assert.IsInstanceOfType<BbCodeElement>(node);
        return (BbCodeElement)node;
    }

    private static string Text(BbCodeNode node)
    {
        Assert.IsInstanceOfType<BbCodeText>(node);
        return ((BbCodeText)node).Value;
    }

    [TestMethod]
    public void Parse_Empty_ReturnsNoNodes()
    {
        Assert.AreEqual(0, BbCodeParser.Parse(null).Count);
        Assert.AreEqual(0, BbCodeParser.Parse("").Count);
    }

    [TestMethod]
    public void Parse_PlainText_ReturnsSingleTextNode()
    {
        var nodes = BbCodeParser.Parse("hello world");

        Assert.AreEqual(1, nodes.Count);
        Assert.AreEqual("hello world", Text(nodes[0]));
    }

    [TestMethod]
    public void Parse_SimpleTag_ReturnsElementWithChild()
    {
        var nodes = BbCodeParser.Parse("[b]bold[/b]");

        Assert.AreEqual(1, nodes.Count);
        var element = Element(nodes[0]);
        Assert.AreEqual("b", element.Tag);
        Assert.AreEqual("bold", Text(element.Children[0]));
    }

    [TestMethod]
    public void Parse_NestedTags_KeepsHierarchy()
    {
        var nodes = BbCodeParser.Parse("[b][i]x[/i][/b]");

        var bold = Element(nodes[0]);
        var italic = Element(bold.Children[0]);
        Assert.AreEqual("i", italic.Tag);
        Assert.AreEqual("x", Text(italic.Children[0]));
    }

    [TestMethod]
    public void Parse_TagWithAttribute_CapturesValue()
    {
        var nodes = BbCodeParser.Parse("[url=https://example.com/a?b=c]link[/url]");

        var url = Element(nodes[0]);
        Assert.AreEqual("url", url.Tag);
        Assert.AreEqual("https://example.com/a?b=c", url.Attribute);
        Assert.AreEqual("link", Text(url.Children[0]));
    }

    [TestMethod]
    public void Parse_UnknownTag_StaysLiteral()
    {
        var nodes = BbCodeParser.Parse("[foo]bar[/foo]");

        Assert.AreEqual(1, nodes.Count);
        Assert.AreEqual("[foo]bar[/foo]", Text(nodes[0]));
    }

    [TestMethod]
    public void Parse_UnmatchedCloser_StaysLiteral()
    {
        var nodes = BbCodeParser.Parse("hello[/b]");

        Assert.AreEqual(1, nodes.Count);
        Assert.AreEqual("hello[/b]", Text(nodes[0]));
    }

    [TestMethod]
    public void Parse_UnclosedTag_ClosesAtEnd()
    {
        var nodes = BbCodeParser.Parse("[b]bold");

        Assert.AreEqual(1, nodes.Count);
        var element = Element(nodes[0]);
        Assert.AreEqual("b", element.Tag);
        Assert.AreEqual("bold", Text(element.Children[0]));
    }

    [TestMethod]
    public void Parse_UnterminatedBracket_StaysLiteral()
    {
        var nodes = BbCodeParser.Parse("2 [ 3 and [b unclosed");

        Assert.AreEqual(1, nodes.Count);
        Assert.AreEqual("2 [ 3 and [b unclosed", Text(nodes[0]));
    }

    [TestMethod]
    public void Parse_NoParse_KeepsBodyVerbatim()
    {
        var nodes = BbCodeParser.Parse("[noparse][b]raw[/b][/noparse]");

        var element = Element(nodes[0]);
        Assert.AreEqual("noparse", element.Tag);
        Assert.AreEqual("[b]raw[/b]", Text(element.Children[0]));
    }

    [TestMethod]
    public void Parse_Code_PreservesSpacingAndTags()
    {
        var nodes = BbCodeParser.Parse("[code]a    [i]b[/i][/code]");

        var element = Element(nodes[0]);
        Assert.AreEqual("code", element.Tag);
        Assert.AreEqual("a    [i]b[/i]", Text(element.Children[0]));
    }

    [TestMethod]
    public void Parse_List_SplitsItemsOnStar()
    {
        var nodes = BbCodeParser.Parse("[list]\n[*]one\n[*]two\n[/list]");

        var list = Element(nodes[0]);
        Assert.AreEqual("list", list.Tag);

        var items = list.Children.OfType<BbCodeElement>().Where(c => c.Tag == "*").ToList();
        Assert.AreEqual(2, items.Count);
        StringAssert.Contains(Text(items[0].Children[0]), "one");
        StringAssert.Contains(Text(items[1].Children[0]), "two");
    }

    [TestMethod]
    public void Parse_HorizontalRule_ReturnsEmptyElement()
    {
        var nodes = BbCodeParser.Parse("[hr][/hr]");

        var element = Element(nodes[0]);
        Assert.AreEqual("hr", element.Tag);
        Assert.AreEqual(0, element.Children.Count);
    }

    [TestMethod]
    public void Parse_CrossedTags_ClosesInnerImplicitly()
    {
        // Steam tolerates sloppy nesting; the inner tag must not leak outside its parent.
        var nodes = BbCodeParser.Parse("[b]a[i]b[/b]c[/i]");

        Assert.AreEqual(2, nodes.Count);
        var bold = Element(nodes[0]);
        Assert.AreEqual("b", bold.Tag);
        Assert.AreEqual("i", Element(bold.Children[1]).Tag);
        Assert.AreEqual("c[/i]", Text(nodes[1]));
    }

    [TestMethod]
    public void Parse_Table_KeepsRowsAndCells()
    {
        var nodes = BbCodeParser.Parse("[table][tr][th]H[/th][/tr][tr][td]C[/td][/tr][/table]");

        var table = Element(nodes[0]);
        var rows = table.Children.OfType<BbCodeElement>().Where(c => c.Tag == "tr").ToList();
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("th", Element(rows[0].Children[0]).Tag);
        Assert.AreEqual("C", Text(Element(rows[1].Children[0]).Children[0]));
    }

    [TestMethod]
    public void Parse_TagNamesAreCaseInsensitive()
    {
        var nodes = BbCodeParser.Parse("[B]bold[/b]");

        var element = Element(nodes[0]);
        Assert.AreEqual("B", element.Tag);
        Assert.AreEqual("bold", Text(element.Children[0]));
    }
}