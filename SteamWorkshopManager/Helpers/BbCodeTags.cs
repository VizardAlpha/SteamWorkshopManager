using System.Collections.Generic;
using WorkshopManager.Models;

namespace WorkshopManager.Helpers;

public static class BbCodeTags
{
    public static IReadOnlyList<MarkupTag> Formatting { get; } =
    [
        new("B", "[b]", "[/b]", TooltipKey: "MarkupBold"),
        new("I", "[i]", "[/i]", TooltipKey: "MarkupItalic"),
        new("U", "[u]", "[/u]", TooltipKey: "MarkupUnderline"),
        new("S", "[strike]", "[/strike]", TooltipKey: "MarkupStrikethrough"),
        new("Spoiler", "[spoiler]", "[/spoiler]", TooltipKey: "MarkupSpoiler"),
        new("NoP", "[noparse]", "[/noparse]", TooltipKey: "MarkupNoParse"),
    ];

    public static IReadOnlyList<MarkupTag> Structure { get; } =
    [
        new("H1", "[h1]", "[/h1]", "MarkupSampleHeading", "MarkupHeading1"),
        new("H2", "[h2]", "[/h2]", "MarkupSampleHeading", "MarkupHeading2"),
        new("H3", "[h3]", "[/h3]", "MarkupSampleHeading", "MarkupHeading3"),
        new("HR", "[hr][/hr]", "", TooltipKey: "MarkupHorizontalRule"),
        new("List", "[list]\n[*]", "\n[/list]", "MarkupSampleItem", "MarkupBulletList"),
        new("OList", "[olist]\n[*]", "\n[/olist]", "MarkupSampleItem", "MarkupOrderedList"),
    ];

    public static IReadOnlyList<MarkupTag> Content { get; } =
    [
        new("URL", "[url=https://]", "[/url]", "MarkupSampleLink", "MarkupLink"),
        new("Img", "[img]", "[/img]", TooltipKey: "MarkupImage", Placeholder: "https://"),
        new("Quote", "[quote=author]", "[/quote]", "MarkupSampleQuote", "MarkupQuoteWithAuthor"),
        new("Code", "[code]", "[/code]", TooltipKey: "MarkupCodeBlock"),
        new("Table", "[table]\n[tr]\n[td]", "[/td]\n[/tr]\n[/table]", "MarkupSampleCell", "MarkupTable"),
    ];
}
