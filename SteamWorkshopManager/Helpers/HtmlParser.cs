using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Tolerant parser for the HTML mod.io descriptions use, good enough for a preview: unclosed
/// tags are closed at the end, stray closers are ignored, a new paragraph or list item ends
/// the previous one, scripts and styles are dropped.
/// </summary>
public static class HtmlParser
{
    private static readonly HashSet<string> VoidTags = ["br", "hr", "img", "wbr", "input", "meta", "link", "source", "col"];
    private static readonly HashSet<string> SkippedTags = ["script", "style", "template", "iframe", "object"];

    // Opening one of these ends an open <p>, as browsers do.
    private static readonly HashSet<string> ClosesParagraph =
        ["p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "hr", "table", "figure", "section"];

    public static IReadOnlyList<HtmlNode> Parse(string? html)
    {
        var root = new List<HtmlNode>();
        if (string.IsNullOrEmpty(html)) return root;

        var stack = new List<Frame>();
        var text = new StringBuilder();
        var i = 0;

        List<HtmlNode> Current() => stack.Count > 0 ? stack[^1].Children : root;

        void FlushText()
        {
            if (text.Length == 0) return;
            Current().Add(new HtmlText(WebUtility.HtmlDecode(text.ToString())));
            text.Clear();
        }

        void CloseTop()
        {
            var frame = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            Current().Add(new HtmlElement(frame.Tag, frame.Attributes, frame.Children));
        }

        void CloseUpTo(string tag)
        {
            var index = stack.FindLastIndex(f => f.Tag == tag);
            if (index < 0) return;
            while (stack.Count > index) CloseTop();
        }

        // Closes an open element of one of these tags, unless a list or table boundary is in between.
        void CloseOpen(string[] tags, string[] boundaries)
        {
            for (var k = stack.Count - 1; k >= 0; k--)
            {
                if (boundaries.Contains(stack[k].Tag)) return;
                if (tags.Contains(stack[k].Tag))
                {
                    CloseUpTo(stack[k].Tag);
                    return;
                }
            }
        }

        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                text.Append(html, i, html.Length - i);
                break;
            }

            text.Append(html, i, lt - i);

            if (string.CompareOrdinal(html, lt, "<!--", 0, 4) == 0)
            {
                var end = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }

            var gt = html.IndexOf('>', lt + 1);
            if (gt < 0 || lt + 1 >= html.Length || !(char.IsLetter(html[lt + 1]) || html[lt + 1] is '/' or '!' or '?'))
            {
                // A lone '<' is just text.
                text.Append('<');
                i = lt + 1;
                continue;
            }

            var inner = html.Substring(lt + 1, gt - lt - 1);
            i = gt + 1;

            if (inner[0] is '!' or '?') continue;

            if (inner[0] == '/')
            {
                var closing = ReadName(inner, 1, out _);
                FlushText();
                CloseUpTo(closing);
                continue;
            }

            var tag = ReadName(inner, 0, out var afterName);
            if (tag.Length == 0)
            {
                text.Append('<').Append(inner).Append('>');
                continue;
            }

            FlushText();

            if (SkippedTags.Contains(tag))
            {
                var closer = html.IndexOf($"</{tag}", i, StringComparison.OrdinalIgnoreCase);
                var end = closer < 0 ? -1 : html.IndexOf('>', closer);
                i = end < 0 ? html.Length : end + 1;
                continue;
            }

            if (ClosesParagraph.Contains(tag)) CloseOpen(["p"], ["li", "td", "th", "blockquote"]);
            if (tag == "li") CloseOpen(["li"], ["ul", "ol"]);
            if (tag is "td" or "th") CloseOpen(["td", "th"], ["tr", "table"]);
            if (tag == "tr") CloseOpen(["tr"], ["table"]);

            var attributes = ReadAttributes(inner, afterName);
            var selfClosing = inner.EndsWith('/');

            if (VoidTags.Contains(tag) || selfClosing)
                Current().Add(new HtmlElement(tag, attributes, []));
            else
                stack.Add(new Frame(tag, attributes));
        }

        FlushText();
        while (stack.Count > 0) CloseTop();
        return root;
    }

    private static string ReadName(string inner, int start, out int end)
    {
        end = start;
        while (end < inner.Length && (char.IsLetterOrDigit(inner[end]) || inner[end] is '-' or ':')) end++;
        return inner[start..end].ToLowerInvariant();
    }

    private static Dictionary<string, string> ReadAttributes(string inner, int start)
    {
        var attributes = new Dictionary<string, string>();
        var i = start;

        while (i < inner.Length)
        {
            while (i < inner.Length && (char.IsWhiteSpace(inner[i]) || inner[i] == '/')) i++;
            var nameStart = i;
            while (i < inner.Length && !char.IsWhiteSpace(inner[i]) && inner[i] is not ('=' or '/')) i++;
            if (i == nameStart) break;
            var name = inner[nameStart..i].ToLowerInvariant();

            while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
            var value = string.Empty;
            if (i < inner.Length && inner[i] == '=')
            {
                i++;
                while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
                if (i < inner.Length && inner[i] is '"' or '\'')
                {
                    var quote = inner[i];
                    var end = inner.IndexOf(quote, i + 1);
                    if (end < 0) end = inner.Length;
                    value = inner[(i + 1)..end];
                    i = Math.Min(end + 1, inner.Length);
                }
                else
                {
                    var valueStart = i;
                    while (i < inner.Length && !char.IsWhiteSpace(inner[i])) i++;
                    value = inner[valueStart..i];
                }
            }

            attributes.TryAdd(name, WebUtility.HtmlDecode(value));
        }

        return attributes;
    }

    private sealed class Frame(string tag, IReadOnlyDictionary<string, string> attributes)
    {
        public string Tag { get; } = tag;
        public IReadOnlyDictionary<string, string> Attributes { get; } = attributes;
        public List<HtmlNode> Children { get; } = [];
    }
}
