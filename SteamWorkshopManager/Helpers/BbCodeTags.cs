using System.Collections.Generic;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Helpers;

public static class BbCodeTags
{
    public static IReadOnlyList<BbCodeTag> Formatting { get; } =
    [
        new("B", "[b]", "[/b]", Tooltip: "Bold text"),
        new("I", "[i]", "[/i]", Tooltip: "Italic text"),
        new("U", "[u]", "[/u]", Tooltip: "Underlined text"),
        new("S", "[strike]", "[/strike]", Tooltip: "Strikethrough text"),
        new("Spoiler", "[spoiler]", "[/spoiler]", Tooltip: "Spoiler"),
        new("NoP", "[noparse]", "[/noparse]", Tooltip: "Doesn't parse [b]tags[/b]"),
    ];

    public static IReadOnlyList<BbCodeTag> Structure { get; } =
    [
        new("H1", "[h1]", "[/h1]", "Heading", "Heading 1"),
        new("H2", "[h2]", "[/h2]", "Heading", "Heading 2"),
        new("H3", "[h3]", "[/h3]", "Heading", "Heading 3"),
        new("HR", "[hr][/hr]", "", Tooltip: "Render a horizontal rule"),
        new("List", "[list]\n[*]", "\n[/list]", "Item", "Bullet List"),
        new("OList", "[olist]\n[*]", "\n[/olist]", "Item", "Ordered list"),
    ];

    public static IReadOnlyList<BbCodeTag> Content { get; } =
    [
        new("URL", "[url=https://]", "[/url]", "link", "Website link"),
        new("Img", "[img]", "[/img]", "https://", "Image"),
        new("Quote", "[quote=author]", "[/quote]", "Quoted text", "Originally posted by author:\nQuoted text"),
        new("Code", "[code]", "[/code]", Tooltip: "Fixed-width font,\npreserves    spaces"),
        new("Table", "[table]\n[tr]\n[td]", "[/td]\n[/tr]\n[/table]", "Cell", "Table"),
    ];
}
