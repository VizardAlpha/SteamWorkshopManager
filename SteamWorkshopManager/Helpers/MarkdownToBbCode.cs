using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Converts the small Markdown subset used in GitHub release notes (headings,
/// bullet lists, bold, italic, code, links) to BBCode so <see cref="BbCodeRenderer"/> can display it.
/// </summary>
public static partial class MarkdownToBbCode
{
    public static string Convert(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var output = new List<string>();
        var inList = false;

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var bullet = BulletRegex().Match(line);

            if (bullet.Success)
            {
                if (!inList)
                {
                    output.Add("[list]");
                    inList = true;
                }
                output.Add("[*]" + Inline(bullet.Groups[1].Value));
                continue;
            }

            if (inList)
            {
                output.Add("[/list]");
                inList = false;
            }

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                var level = System.Math.Min(heading.Groups[1].Value.Length, 3);
                output.Add($"[h{level}]{Inline(heading.Groups[2].Value)}[/h{level}]");
            }
            else if (HorizontalRuleRegex().IsMatch(line))
            {
                output.Add("[hr][/hr]");
            }
            else
            {
                output.Add(Inline(line));
            }
        }

        if (inList) output.Add("[/list]");
        return string.Join("\n", output).Trim();
    }

    private static string Inline(string text)
    {
        // Images are dropped: release notes are displayed, not a place to fetch arbitrary content.
        text = ImageRegex().Replace(text, string.Empty);
        text = LinkRegex().Replace(text, m => $"[url={m.Groups[2].Value}]{m.Groups[1].Value}[/url]");
        text = CodeRegex().Replace(text, "$1");
        text = BoldRegex().Replace(text, "[b]$2[/b]");
        text = ItalicRegex().Replace(text, "[i]$2[/i]");
        return text;
    }

    [GeneratedRegex(@"^\s*[-*+]\s+(.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"(\*\*|__)(.+?)\1")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<![\w*])(\*|_)(?!\s)(.+?)(?<!\s)\1(?![\w*])")]
    private static partial Regex ItalicRegex();
}
