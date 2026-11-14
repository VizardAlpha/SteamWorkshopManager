using System.Collections.Generic;
using WorkshopManager.Models;

namespace WorkshopManager.Helpers;

/// <summary>Toolbar of the HTML editor (mod.io descriptions), grouped like <see cref="BbCodeTags"/>.</summary>
public static class HtmlTags
{
    public static IReadOnlyList<MarkupTag> Formatting { get; } =
    [
        new("B", "<strong>", "</strong>", TooltipKey: "MarkupBold"),
        new("I", "<em>", "</em>", TooltipKey: "MarkupItalic"),
        new("U", "<u>", "</u>", TooltipKey: "MarkupUnderline"),
        new("S", "<s>", "</s>", TooltipKey: "MarkupStrikethrough"),
        new("Code", "<code>", "</code>", TooltipKey: "MarkupInlineCode"),
    ];

    public static IReadOnlyList<MarkupTag> Structure { get; } =
    [
        new("P", "<p>", "</p>", "MarkupSampleParagraph", "MarkupParagraph"),
        new("BR", "<br>", "", TooltipKey: "MarkupLineBreak"),
        new("H1", "<h1>", "</h1>", "MarkupSampleHeading", "MarkupHeading1"),
        new("H2", "<h2>", "</h2>", "MarkupSampleHeading", "MarkupHeading2"),
        new("H3", "<h3>", "</h3>", "MarkupSampleHeading", "MarkupHeading3"),
        new("HR", "<hr>", "", TooltipKey: "MarkupHorizontalRule"),
        new("List", "<ul>\n<li>", "</li>\n</ul>", "MarkupSampleItem", "MarkupBulletList"),
        new("OList", "<ol>\n<li>", "</li>\n</ol>", "MarkupSampleItem", "MarkupOrderedList"),
    ];

    public static IReadOnlyList<MarkupTag> Content { get; } =
    [
        new("Link", "<a href=\"https://\">", "</a>", "MarkupSampleLink", "MarkupLink"),
        new("Img", "<img src=\"", "\" alt=\"\">", TooltipKey: "MarkupImage", Placeholder: "https://"),
        new("Quote", "<blockquote>", "</blockquote>", "MarkupSampleQuote", "MarkupQuote"),
        new("Pre", "<pre>", "</pre>", TooltipKey: "MarkupPreBlock"),
    ];
}
