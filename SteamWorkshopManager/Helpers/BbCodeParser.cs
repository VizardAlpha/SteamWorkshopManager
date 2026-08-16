using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Parses the BBCode subset Steam renders on Workshop pages into a node tree.
/// Unknown tags and unmatched closers are kept as literal text, the way Steam shows them.
/// </summary>
public static class BbCodeParser
{
    private static readonly HashSet<string> KnownTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "i", "u", "strike", "spoiler", "noparse",
        "h1", "h2", "h3", "hr", "list", "olist", "*",
        "url", "img", "quote", "code",
        "table", "tr", "th", "td", "previewyoutube",
    };

    // Bodies of these tags are taken verbatim, nested tags are not parsed.
    private static readonly HashSet<string> RawTags = new(StringComparer.OrdinalIgnoreCase) { "noparse", "code" };

    public static IReadOnlyList<BbCodeNode> Parse(string? text)
    {
        var root = new List<BbCodeNode>();
        if (string.IsNullOrEmpty(text)) return root;

        var stack = new Stack<Frame>();
        var literal = new StringBuilder();
        var i = 0;

        List<BbCodeNode> Current() => stack.Count > 0 ? stack.Peek().Children : root;

        void FlushLiteral()
        {
            if (literal.Length == 0) return;
            Current().Add(new BbCodeText(literal.ToString()));
            literal.Clear();
        }

        while (i < text.Length)
        {
            var open = text.IndexOf('[', i);
            if (open < 0)
            {
                literal.Append(text, i, text.Length - i);
                break;
            }

            literal.Append(text, i, open - i);

            var close = text.IndexOf(']', open + 1);
            if (close < 0)
            {
                literal.Append(text, open, text.Length - open);
                break;
            }

            var inner = text.Substring(open + 1, close - open - 1);

            // A nested '[' means this bracket never opened a tag; rescan from the inner one.
            if (inner.Length == 0 || inner.Contains('['))
            {
                literal.Append('[');
                i = open + 1;
                continue;
            }

            if (inner[0] == '/')
            {
                var closing = inner[1..].Trim();
                if (stack.Any(f => string.Equals(f.Tag, closing, StringComparison.OrdinalIgnoreCase)))
                {
                    FlushLiteral();
                    CloseUpTo(stack, root, closing);
                }
                else
                {
                    literal.Append(text, open, close - open + 1);
                }

                i = close + 1;
                continue;
            }

            var eq = inner.IndexOf('=');
            var tag = (eq < 0 ? inner : inner[..eq]).Trim();
            var attribute = eq < 0 ? null : inner[(eq + 1)..].Trim().Trim('"');

            if (!KnownTags.Contains(tag))
            {
                literal.Append(text, open, close - open + 1);
                i = close + 1;
                continue;
            }

            FlushLiteral();

            if (RawTags.Contains(tag))
            {
                var body = ReadRawBody(text, close + 1, tag, out var next);
                Current().Add(new BbCodeElement(tag, attribute, [new BbCodeText(body)]));
                i = next;
                continue;
            }

            // [*] has no closer: a new item ends the previous one.
            if (tag == "*" && stack.Count > 0 && stack.Peek().Tag == "*")
                CloseTop(stack, root);

            stack.Push(new Frame(tag, attribute));
            i = close + 1;
        }

        FlushLiteral();
        while (stack.Count > 0) CloseTop(stack, root);

        return root;
    }

    private static void CloseTop(Stack<Frame> stack, List<BbCodeNode> root)
    {
        var frame = stack.Pop();
        var parent = stack.Count > 0 ? stack.Peek().Children : root;
        parent.Add(new BbCodeElement(frame.Tag, frame.Attribute, frame.Children));
    }

    private static void CloseUpTo(Stack<Frame> stack, List<BbCodeNode> root, string tag)
    {
        while (stack.Count > 0)
        {
            var closed = stack.Peek().Tag;
            CloseTop(stack, root);
            if (string.Equals(closed, tag, StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    private static string ReadRawBody(string text, int start, string tag, out int next)
    {
        var closer = $"[/{tag}]";
        var end = text.IndexOf(closer, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            next = text.Length;
            return text[start..];
        }

        next = end + closer.Length;
        return text[start..end];
    }

    private sealed class Frame(string tag, string? attribute)
    {
        public string Tag { get; } = tag;
        public string? Attribute { get; } = attribute;
        public List<BbCodeNode> Children { get; } = [];
    }
}