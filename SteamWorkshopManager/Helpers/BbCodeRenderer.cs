using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Turns a BBCode node tree into Avalonia controls that mimic how Steam renders
/// a Workshop description. Colors follow Steam's own palette so the preview reads
/// as the store page rather than as app chrome.
/// </summary>
public static class BbCodeRenderer
{
    private static readonly Logger Log = LogService.GetLogger<object>();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, Bitmap> ImageCache = new();

    // Values below are lifted from Steam's own CSS (shared_global.css, workshop.css)
    // so the preview matches what the Workshop page will actually show.
    internal static readonly IBrush Background = new SolidColorBrush(Color.Parse("#1b2838"));
    internal static readonly IBrush Outline = new SolidColorBrush(Color.Parse("#2a3440"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#acb2b8"));
    internal static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8f98a0"));
    private static readonly IBrush HeadingBrush = new SolidColorBrush(Color.Parse("#5aa9d6"));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#ebebeb"));
    private static readonly IBrush LinkHostBrush = new SolidColorBrush(Color.Parse("#7e8391"));
    private static readonly IBrush SpoilerBrush = new SolidColorBrush(Color.Parse("#000000"));
    private static readonly IBrush SpoilerRevealedBrush = new SolidColorBrush(Color.Parse("#ffffff"));
    private static readonly IBrush QuoteBorder = new SolidColorBrush(Color.Parse("#56707f"));
    private static readonly IBrush CodeBorder = new SolidColorBrush(Color.Parse("#535354"));
    private static readonly IBrush TableBorder = new SolidColorBrush(Color.Parse("#4d4d4d"));
    private static readonly IBrush RuleBrush = new SolidColorBrush(Color.Parse("#4d4d4d"));

    private static readonly FontFamily MonoFont = new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    private const double BodyFontSize = 14;
    private const double BodyLineHeight = 20;

    public static Control Render(string? bbCode)
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.SetValue(TextElement.FontSizeProperty, BodyFontSize);
        panel.SetValue(TextElement.ForegroundProperty, TextBrush);

        RenderBlocks(BbCodeParser.Parse(bbCode), panel);
        return panel;
    }

    private static bool IsBlock(string tag) => tag.ToLowerInvariant()
        is "h1" or "h2" or "h3" or "hr" or "list" or "olist"
        or "quote" or "code" or "table" or "img" or "previewyoutube";

    private static void RenderBlocks(IReadOnlyList<BbCodeNode> nodes, Panel target)
    {
        var buffer = new List<BbCodeNode>();

        void Flush()
        {
            // Steam turns every newline into a <br>, blank lines included, so nothing is trimmed here.
            if (buffer.Count > 0) target.Children.Add(CreateParagraph(buffer));
            buffer.Clear();
        }

        foreach (var node in nodes)
        {
            if (node is BbCodeElement element && IsBlock(element.Tag))
            {
                Flush();
                target.Children.Add(CreateBlock(element));
                // A [hr] left open swallows the rest of the text; render it after the rule.
                if (string.Equals(element.Tag, "hr", StringComparison.OrdinalIgnoreCase))
                    RenderBlocks(element.Children, target);
            }
            else
            {
                buffer.Add(node);
            }
        }

        Flush();
    }

    private static Control RenderContent(IReadOnlyList<BbCodeNode> nodes)
    {
        var panel = new StackPanel { Spacing = 0 };
        RenderBlocks(nodes, panel);
        return panel;
    }

    #region Blocks

    private static Control CreateBlock(BbCodeElement element) => element.Tag.ToLowerInvariant() switch
    {
        "h1" => CreateHeading(element, 20, 23, FontWeight.Normal, new Thickness(0, 0, 0, 10)),
        "h2" => CreateHeading(element, 18, 21, FontWeight.Normal, new Thickness(0, 8, 0, 6)),
        "h3" => CreateHeading(element, 16, 19, FontWeight.Light, new Thickness(0, 8, 0, 6)),
        "hr" => new Border { Height = 1, Background = RuleBrush, Margin = new Thickness(0, 8) },
        "list" => CreateList(element, ordered: false),
        "olist" => CreateList(element, ordered: true),
        "quote" => CreateQuote(element),
        "code" => CreateCode(element),
        "table" => CreateTable(element),
        "img" => CreateImage(element),
        "previewyoutube" => CreateYoutubeLink(element),
        _ => RenderContent(element.Children),
    };

    private static Control CreateHeading(BbCodeElement element, double fontSize, double lineHeight, FontWeight weight, Thickness margin)
    {
        var heading = CreateParagraph(element.Children);
        heading.FontSize = fontSize;
        heading.LineHeight = lineHeight;
        heading.FontWeight = weight;
        heading.Foreground = HeadingBrush;
        heading.Margin = margin;
        return heading;
    }

    private static Control CreateList(BbCodeElement element, bool ordered)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(10, 2, 0, 2) };
        var index = 1;

        foreach (var item in element.Children.OfType<BbCodeElement>().Where(c => c.Tag == "*"))
        {
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*") };

            var bullet = new TextBlock
            {
                Text = ordered ? $"{index++}." : "•",
                LineHeight = BodyLineHeight,
                MinWidth = ordered ? 18 : 12,
                Margin = new Thickness(0, 0, 6, 0),
            };

            var content = RenderContent(item.Children);
            Grid.SetColumn(bullet, 0);
            Grid.SetColumn(content, 1);
            row.Children.Add(bullet);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control CreateQuote(BbCodeElement element)
    {
        var panel = new StackPanel { Spacing = 0 };

        if (!string.IsNullOrWhiteSpace(element.Attribute))
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationService.GetString("BbCodeQuoteAuthor"), element.Attribute),
                FontStyle = FontStyle.Italic,
                LineHeight = BodyLineHeight,
            });
        }

        RenderBlocks(element.Children, panel);

        var quote = new Border
        {
            BorderBrush = QuoteBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(12),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = panel,
        };

        // Steam renders quotes at 92% of the surrounding text.
        quote.SetValue(TextElement.FontSizeProperty, BodyFontSize * 0.92);
        return quote;
    }

    private static Control CreateCode(BbCodeElement element) => new Border
    {
        BorderBrush = CodeBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(12),
        Margin = new Thickness(8),
        Child = new TextBlock
        {
            Text = PlainText(element.Children).Trim('\r', '\n'),
            FontFamily = MonoFont,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        },
    };

    private static Control CreateTable(BbCodeElement element)
    {
        var rows = element.Children.OfType<BbCodeElement>()
            .Where(c => string.Equals(c.Tag, "tr", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (rows.Count == 0) return RenderContent(element.Children);

        var cellsPerRow = rows
            .Select(r => r.Children.OfType<BbCodeElement>()
                .Where(c => c.Tag.Equals("td", StringComparison.OrdinalIgnoreCase) ||
                            c.Tag.Equals("th", StringComparison.OrdinalIgnoreCase))
                .ToList())
            .ToList();

        var columns = cellsPerRow.Max(c => c.Count);
        if (columns == 0) return RenderContent(element.Children);

        var grid = new Grid { Margin = new Thickness(0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        grid.SetValue(TextElement.FontSizeProperty, 12d);
        for (var c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var r = 0; r < cellsPerRow.Count; r++)
        {
            for (var c = 0; c < cellsPerRow[r].Count; c++)
            {
                var cell = cellsPerRow[r][c];
                var content = RenderContent(cell.Children);
                if (cell.Tag.Equals("th", StringComparison.OrdinalIgnoreCase))
                    TextElement.SetFontWeight(content, FontWeight.SemiBold);

                var border = new Border
                {
                    BorderBrush = TableBorder,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4),
                    Child = content,
                };

                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }

        return grid;
    }

    private static Control CreateImage(BbCodeElement element)
    {
        var url = (element.Attribute ?? PlainText(element.Children)).Trim();

        var image = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = 630,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4),
        };

        _ = LoadImageAsync(image, url);
        return image;
    }

    private static Control CreateYoutubeLink(BbCodeElement element)
    {
        var id = (element.Attribute ?? PlainText(element.Children)).Split(';')[0].Trim();
        return CreateLinkControl($"▶ youtube.com/watch?v={id}", $"https://www.youtube.com/watch?v={id}");
    }

    #endregion

    #region Inlines

    private static TextBlock CreateParagraph(IReadOnlyList<BbCodeNode> nodes)
    {
        // Font size and foreground are inherited from the root panel so nested
        // scopes (quotes, tables) can scale their whole subtree at once.
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = BodyLineHeight,
        };

        block.Inlines ??= new InlineCollection();
        AppendInlines(nodes, block.Inlines);
        return block;
    }

    private static void AppendInlines(IReadOnlyList<BbCodeNode> nodes, InlineCollection target)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case BbCodeText text:
                    AppendText(text.Value, target);
                    break;
                case BbCodeElement element:
                    AppendElement(element, target);
                    break;
            }
        }
    }

    private static void AppendText(string value, InlineCollection target)
    {
        var lines = value.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) target.Add(new LineBreak());
            if (lines[i].Length > 0) target.Add(new Run(lines[i]));
        }
    }

    private static void AppendElement(BbCodeElement element, InlineCollection target)
    {
        switch (element.Tag.ToLowerInvariant())
        {
            case "b":
                AppendSpan(new Bold(), element, target);
                break;
            case "i":
                AppendSpan(new Italic(), element, target);
                break;
            case "u":
                AppendSpan(new Underline(), element, target);
                break;
            case "strike":
                AppendSpan(new Span { TextDecorations = TextDecorations.Strikethrough }, element, target);
                break;
            case "spoiler":
                target.Add(new InlineUIContainer(CreateSpoiler(element)));
                break;
            case "url":
                AppendLink(element, target);
                break;
            default:
                // [noparse] and block tags nested inline: keep the children.
                AppendInlines(element.Children, target);
                break;
        }
    }

    private static void AppendSpan(Span span, BbCodeElement element, InlineCollection target)
    {
        AppendInlines(element.Children, span.Inlines);
        target.Add(span);
    }

    private static Control CreateSpoiler(BbCodeElement element)
    {
        var content = CreateParagraph(element.Children);
        content.Foreground = SpoilerBrush;

        var border = new Border
        {
            Background = SpoilerBrush,
            Padding = new Thickness(8, 0),
            Child = content,
        };

        ToolTip.SetTip(border, LocalizationService.GetString("BbCodeSpoilerHint"));

        // Steam reveals spoilers on hover, not on click.
        border.PointerEntered += (_, _) => content.Foreground = SpoilerRevealedBrush;
        border.PointerExited += (_, _) => content.Foreground = SpoilerBrush;

        return border;
    }

    private static void AppendLink(BbCodeElement element, InlineCollection target)
    {
        var url = (element.Attribute ?? PlainText(element.Children)).Trim();
        var label = PlainText(element.Children).Trim();
        if (label.Length == 0) label = url;

        target.Add(new InlineUIContainer(CreateLinkControl(label, url)));

        // Steam appends the host of off-site links right after the label.
        var host = HostOf(url);
        if (host is not null && !host.Contains("steam", StringComparison.OrdinalIgnoreCase))
            target.Add(new Run($"[{host}]") { FontSize = 10, Foreground = LinkHostBrush });
    }

    private static string? HostOf(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host;
        return Uri.TryCreate($"https://{url}", UriKind.Absolute, out var fallback) ? fallback.Host : null;
    }

    private static Control CreateLinkControl(string label, string url)
    {
        var link = new TextBlock
        {
            Text = label,
            Foreground = LinkBrush,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        // Steam links are not underlined until hovered.
        link.PointerEntered += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.PointerExited += (_, _) => link.TextDecorations = null;

        ToolTip.SetTip(link, url);
        link.PointerPressed += (_, _) => OpenUrl(url);
        return link;
    }

    #endregion

    private static string PlainText(IReadOnlyList<BbCodeNode> nodes)
    {
        var builder = new StringBuilder();
        Collect(nodes);
        return builder.ToString();

        void Collect(IReadOnlyList<BbCodeNode> current)
        {
            foreach (var node in current)
            {
                switch (node)
                {
                    case BbCodeText text:
                        builder.Append(text.Value);
                        break;
                    case BbCodeElement element:
                        Collect(element.Children);
                        break;
                }
            }
        }
    }

    private static void OpenUrl(string url)
    {
        if (!IsWebUrl(url)) return;

        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Debug($"BbCodeRenderer: failed to open URL {url}: {ex.Message}"); }
    }

    private static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static async Task LoadImageAsync(Image target, string url)
    {
        if (!IsWebUrl(url)) return;

        if (ImageCache.TryGetValue(url, out var cached))
        {
            target.Source = cached;
            return;
        }

        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            ImageCache[url] = bitmap;
            await Dispatcher.UIThread.InvokeAsync(() => target.Source = bitmap);
        }
        catch (Exception ex)
        {
            Log.Debug($"BbCodeRenderer: image fetch failed for {url}: {ex.Message}");
        }
    }
}