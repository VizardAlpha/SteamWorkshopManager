using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class HtmlParserTests
{
    private static HtmlElement Element(HtmlNode node) => (HtmlElement)node;

    [TestMethod]
    public void Parses_NestedTagsAttributesAndEntities()
    {
        var nodes = HtmlParser.Parse("""<p>Hi <a href="https://mod.io?a=1&amp;b=2">mod <strong>io</strong></a> &lt;3</p>""");

        var p = Element(nodes.Single());
        Assert.AreEqual("p", p.Tag);
        var link = Element(p.Children[1]);
        Assert.AreEqual("https://mod.io?a=1&b=2", link.Attribute("href"));
        Assert.AreEqual("strong", Element(link.Children[1]).Tag);
        Assert.AreEqual(" <3", ((HtmlText)p.Children[2]).Value);
    }

    [TestMethod]
    public void NewParagraphOrItem_ClosesThePreviousOne()
    {
        var nodes = HtmlParser.Parse("<p>one<p>two<ul><li>a<li>b</ul>");

        Assert.AreEqual(3, nodes.Count);
        Assert.AreEqual("one", ((HtmlText)Element(nodes[0]).Children.Single()).Value);
        var list = Element(nodes[2]);
        Assert.AreEqual(2, list.Children.Count);
        Assert.IsTrue(list.Children.All(c => Element(c).Tag == "li"));
    }

    [TestMethod]
    public void VoidAndSelfClosingTags_HaveNoChildren()
    {
        var nodes = HtmlParser.Parse("""a<br>b<br/>c<img src="x.png" alt='logo'><hr>""");

        Assert.AreEqual("br", Element(nodes[1]).Tag);
        Assert.AreEqual("br", Element(nodes[3]).Tag);
        Assert.AreEqual("logo", Element(nodes[5]).Attribute("alt"));
        Assert.AreEqual("hr", Element(nodes[6]).Tag);
    }

    [TestMethod]
    public void ScriptsStylesAndComments_AreDropped()
    {
        var nodes = HtmlParser.Parse("<p>a<!-- note --><script>alert(1)</script><style>p{}</style>b</p>");

        var texts = Element(nodes.Single()).Children.OfType<HtmlText>().Select(t => t.Value);
        Assert.AreEqual("ab", string.Concat(texts));
    }

    [TestMethod]
    public void StrayCloserAndLoneBracket_AreTolerated()
    {
        var nodes = HtmlParser.Parse("1 < 2 </em> <b>bold");

        Assert.AreEqual("1 < 2  ", string.Concat(nodes.OfType<HtmlText>().Select(t => t.Value)));
        Assert.AreEqual("b", Element(nodes[^1]).Tag);
    }
}
