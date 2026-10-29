using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Renders a mod.io description (HTML) as Avalonia controls, close to mod.io's dark mod page.
/// Tags it doesn't know keep their content; the source text itself is never changed.
/// </summary>
public static partial class HtmlRenderer
{
    internal static readonly IBrush Background = new SolidColorBrush(Color.Parse("#15171c"));
    internal static readonly IBrush Outline = new SolidColorBrush(Color.Parse("#2b2f38"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#c9ccd3"));
    internal static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8a909c"));
    private static readonly IBrush HeadingBrush = new SolidColorBrush(Color.Parse("#ffffff"));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#07c1d8"));
    private static readonly IBrush QuoteBorder = new SolidColorBrush(Color.Parse("#07c1d8"));
    private static readonly IBrush CodeBackground = new SolidColorBrush(Color.Parse("#1f232b"));
    private static readonly IBrush RuleBrush = new SolidColorBrush(Color.Parse("#2b2f38"));

    private static readonly FontFamily MonoFont = new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    private const double BodyFontSize = 14;
    private const double BodyLineHeight = 21;

    private static readonly HashSet<string> BlockTags =
    [
        "p", "div", "section", "article", "header", "footer", "center", "figure", "figcaption",
        "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "hr", "table",
    ];

    public static Control Render(string? html)
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.SetValue(TextElement.FontSizeProperty, BodyFontSize);
        panel.SetValue(TextElement.ForegroundProperty, TextBrush);
        RenderBlocks(HtmlParser.Parse(html), panel);
        return panel;
    }

    private static bool IsBlock(HtmlNode node) => node is HtmlElement e && BlockTags.Contains(e.Tag);

    /// <summary>Consecutive inline nodes become one paragraph; block elements render on their own.</summary>
    private static void RenderBlocks(IReadOnlyList<HtmlNode> nodes, Panel target)
    {
        var inline = new List<HtmlNode>();

        void Flush()
        {
            if (inline.Count == 0) return;
            var paragraph = CreateParagraph(inline);
            if (paragraph.Inlines!.Count > 0) target.Children.Add(paragraph);
            inline.Clear();
        }

        foreach (var node in nodes)
        {
            if (IsBlock(node))
            {
                Flush();
                target.Children.Add(CreateBlock((HtmlElement)node));
            }
            else
            {
                inline.Add(node);
            }
        }

        Flush();
    }

    private static Control RenderContent(IReadOnlyList<HtmlNode> nodes)
    {
        var panel = new StackPanel { Spacing = 0 };
        RenderBlocks(nodes, panel);
        return panel;
    }

    #region Blocks

    private static Control CreateBlock(HtmlElement element) => element.Tag switch
    {
        "p" => WithMargin(RenderContent(element.Children), new Thickness(0, 0, 0, 12)),
        "h1" => CreateHeading(element, 24, FontWeight.Bold),
        "h2" => CreateHeading(element, 20, FontWeight.Bold),
        "h3" => CreateHeading(element, 17, FontWeight.SemiBold),
        "h4" or "h5" or "h6" => CreateHeading(element, 15, FontWeight.SemiBold),
        "ul" => CreateList(element, ordered: false),
        "ol" => CreateList(element, ordered: true),
        "blockquote" => CreateQuote(element),
        "pre" => CreateCodeBlock(element),
        "hr" => new Border { Height = 1, Background = RuleBrush, Margin = new Thickness(0, 10) },
        "table" => CreateTable(element),
        _ => RenderContent(element.Children),
    };

    private static Control WithMargin(Control control, Thickness margin)
    {
        control.Margin = margin;
        return control;
    }

    private static Control CreateHeading(HtmlElement element, double size, FontWeight weight)
    {
        var heading = CreateParagraph(element.Children);
        heading.FontSize = size;
        heading.LineHeight = size * 1.35;
        heading.FontWeight = weight;
        heading.Foreground = HeadingBrush;
        heading.Margin = new Thickness(0, 6, 0, 8);
        return heading;
    }

    private static Control CreateList(HtmlElement element, bool ordered)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(18, 0, 0, 12) };
        var index = 1;

        foreach (var item in element.Children.OfType<HtmlElement>().Where(c => c.Tag == "li"))
        {
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*") };
            var bullet = new TextBlock
            {
                Text = ordered ? $"{index++}." : "•",
                LineHeight = BodyLineHeight,
                MinWidth = ordered ? 20 : 12,
                Margin = new Thickness(0, 0, 6, 0),
            };
            var content = RenderContent(item.Children);
            Grid.SetColumn(content, 1);
            row.Children.Add(bullet);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control CreateQuote(HtmlElement element) => new Border
    {
        BorderBrush = QuoteBorder,
        BorderThickness = new Thickness(3, 0, 0, 0),
        Padding = new Thickness(12, 4),
        Margin = new Thickness(0, 0, 0, 12),
        Child = RenderContent(element.Children),
    };

    private static Control CreateCodeBlock(HtmlElement element) => new Border
    {
        Background = CodeBackground,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(12, 10),
        Margin = new Thickness(0, 0, 0, 12),
        Child = new TextBlock
        {
            Text = PlainText(element.Children).Trim('\r', '\n'),
            FontFamily = MonoFont,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        },
    };

    private static Control CreateTable(HtmlElement element)
    {
        // Rows may sit directly in <table> or in <thead>/<tbody>/<tfoot>.
        var rows = element.Children.OfType<HtmlElement>()
            .SelectMany(c => c.Tag is "thead" or "tbody" or "tfoot" ? c.Children.OfType<HtmlElement>() : [c])
            .Where(c => c.Tag == "tr")
            .Select(r => r.Children.OfType<HtmlElement>().Where(c => c.Tag is "td" or "th").ToList())
            .ToList();

        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        if (columns == 0) return RenderContent(element.Children);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Left };
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < rows.Count; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                var cell = rows[r][c];
                var content = RenderContent(cell.Children);
                if (cell.Tag == "th") TextElement.SetFontWeight(content, FontWeight.SemiBold);
                var border = new Border
                {
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 4),
                    Child = content,
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }

        return grid;
    }

    #endregion

    #region Inlines

    private static TextBlock CreateParagraph(IReadOnlyList<HtmlNode> nodes)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = BodyLineHeight };
        block.Inlines ??= new InlineCollection();
        var state = new WhitespaceState { AtLineStart = true };
        AppendInlines(nodes, block.Inlines, state);

        // HTML drops the space before the end of a block.
        if (block.Inlines.Count > 0 && block.Inlines[^1] is Run { Text: { } last } run && last.EndsWith(' '))
            run.Text = last.TrimEnd();
        return block;
    }

    /// <summary>HTML collapses runs of whitespace, across inline tags too.</summary>
    private sealed class WhitespaceState
    {
        public bool AtLineStart { get; set; }
        public bool LastWasSpace { get; set; }
    }

    private static void AppendInlines(IReadOnlyList<HtmlNode> nodes, InlineCollection target, WhitespaceState state)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case HtmlText text:
                    AppendText(text.Value, target, state);
                    break;
                case HtmlElement element:
                    AppendElement(element, target, state);
                    break;
            }
        }
    }

    private static void AppendText(string value, InlineCollection target, WhitespaceState state)
    {
        var collapsed = WhitespaceRegex().Replace(value, " ");
        if (collapsed.StartsWith(' ') && (state.AtLineStart || state.LastWasSpace)) collapsed = collapsed[1..];
        if (collapsed.Length == 0) return;

        target.Add(new Run(collapsed));
        state.AtLineStart = false;
        state.LastWasSpace = collapsed.EndsWith(' ');
    }

    private static void AppendElement(HtmlElement element, InlineCollection target, WhitespaceState state)
    {
        switch (element.Tag)
        {
            case "br":
                target.Add(new LineBreak());
                state.AtLineStart = true;
                state.LastWasSpace = false;
                break;
            case "b" or "strong":
                AppendSpan(new Bold(), element, target, state);
                break;
            case "i" or "em":
                AppendSpan(new Italic(), element, target, state);
                break;
            case "u" or "ins":
                AppendSpan(new Underline(), element, target, state);
                break;
            case "s" or "strike" or "del":
                AppendSpan(new Span { TextDecorations = TextDecorations.Strikethrough }, element, target, state);
                break;
            case "code":
                AppendSpan(new Span { FontFamily = MonoFont, Background = CodeBackground }, element, target, state);
                break;
            case "a":
                AppendLink(element, target, state);
                break;
            case "img":
                AppendImage(element, target, state);
                break;
            default:
                if (IsBlock(element))
                {
                    // A block nested in an inline context: keep its content on its own line.
                    target.Add(new InlineUIContainer(CreateBlock(element)));
                    state.AtLineStart = true;
                }
                else
                {
                    AppendInlines(element.Children, target, state);
                }
                break;
        }
    }

    private static void AppendSpan(Span span, HtmlElement element, InlineCollection target, WhitespaceState state)
    {
        AppendInlines(element.Children, span.Inlines, state);
        target.Add(span);
    }

    private static void AppendLink(HtmlElement element, InlineCollection target, WhitespaceState state)
    {
        var url = element.Attribute("href")?.Trim() ?? string.Empty;
        var label = WhitespaceRegex().Replace(PlainText(element.Children), " ").Trim();
        if (label.Length == 0) label = url;

        var link = new TextBlock { Text = label, Foreground = LinkBrush, Cursor = new Cursor(StandardCursorType.Hand) };
        link.PointerEntered += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.PointerExited += (_, _) => link.TextDecorations = null;
        ToolTip.SetTip(link, url);
        link.PointerPressed += (_, _) => BbCodeRenderer.OpenUrl(url);

        target.Add(new InlineUIContainer(link));
        state.AtLineStart = false;
        state.LastWasSpace = false;
    }

    private static void AppendImage(HtmlElement element, InlineCollection target, WhitespaceState state)
    {
        var url = element.Attribute("src")?.Trim() ?? string.Empty;
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = 640,
            Margin = new Thickness(0, 4),
        };
        if (element.Attribute("alt") is { Length: > 0 } alt) ToolTip.SetTip(image, alt);
        _ = BbCodeRenderer.LoadImageAsync(image, url);

        target.Add(new InlineUIContainer(image));
        state.AtLineStart = false;
        state.LastWasSpace = false;
    }

    #endregion

    private static string PlainText(IReadOnlyList<HtmlNode> nodes)
    {
        var builder = new StringBuilder();
        Collect(nodes);
        return builder.ToString();

        void Collect(IReadOnlyList<HtmlNode> current)
        {
            foreach (var node in current)
            {
                switch (node)
                {
                    case HtmlText text:
                        builder.Append(text.Value);
                        break;
                    case HtmlElement { Tag: "br" }:
                        builder.Append('\n');
                        break;
                    case HtmlElement element:
                        Collect(element.Children);
                        break;
                }
            }
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
