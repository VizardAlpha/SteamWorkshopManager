using System.Collections.Generic;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Helpers;

/// <summary>Toolbar of the HTML editor (mod.io descriptions), grouped like <see cref="BbCodeTags"/>.</summary>
public static class HtmlTags
{
    public static IReadOnlyList<MarkupTag> Formatting { get; } =
    [
        new("B", "<strong>", "</strong>", Tooltip: "Bold text"),
        new("I", "<em>", "</em>", Tooltip: "Italic text"),
        new("U", "<u>", "</u>", Tooltip: "Underlined text"),
        new("S", "<s>", "</s>", Tooltip: "Strikethrough text"),
        new("Code", "<code>", "</code>", Tooltip: "Inline code"),
    ];

    public static IReadOnlyList<MarkupTag> Structure { get; } =
    [
        new("P", "<p>", "</p>", "Paragraph", "Paragraph"),
        new("BR", "<br>", "", Tooltip: "Line break"),
        new("H1", "<h1>", "</h1>", "Heading", "Heading 1"),
        new("H2", "<h2>", "</h2>", "Heading", "Heading 2"),
        new("H3", "<h3>", "</h3>", "Heading", "Heading 3"),
        new("HR", "<hr>", "", Tooltip: "Horizontal rule"),
        new("List", "<ul>\n<li>", "</li>\n</ul>", "Item", "Bullet list"),
        new("OList", "<ol>\n<li>", "</li>\n</ol>", "Item", "Ordered list"),
    ];

    public static IReadOnlyList<MarkupTag> Content { get; } =
    [
        new("Link", "<a href=\"https://\">", "</a>", "link", "Website link"),
        new("Img", "<img src=\"", "\" alt=\"\">", "https://", "Image"),
        new("Quote", "<blockquote>", "</blockquote>", "Quoted text", "Quote"),
        new("Pre", "<pre>", "</pre>", Tooltip: "Fixed-width block,\npreserves    spaces"),
    ];
}
