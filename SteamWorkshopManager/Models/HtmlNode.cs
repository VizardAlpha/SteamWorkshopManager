using System.Collections.Generic;

namespace SteamWorkshopManager.Models;

public abstract record HtmlNode;

/// <summary>Text with entities already decoded.</summary>
public sealed record HtmlText(string Value) : HtmlNode;

/// <summary>An element; tag and attribute names are lower case.</summary>
public sealed record HtmlElement(string Tag, IReadOnlyDictionary<string, string> Attributes, IReadOnlyList<HtmlNode> Children) : HtmlNode
{
    public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;
}
