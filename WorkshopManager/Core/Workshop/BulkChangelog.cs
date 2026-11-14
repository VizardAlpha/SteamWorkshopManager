using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WorkshopManager.Helpers;
using WorkshopManager.Models;

namespace WorkshopManager.Core.Workshop;

/// <summary>
/// Change notes of a bulk update: one text for every mod, or a file with a section per mod.
/// A section starts with a level-1 heading holding the mod's title or id ("# My mod");
/// the text before the first heading applies to the mods that have no section.
/// </summary>
public sealed class BulkChangelog
{
    /// <summary>Steam refuses longer change notes.</summary>
    public const int MaxNoteLength = 8000;

    private readonly Dictionary<string, string> _sections;

    private BulkChangelog(string fallback, Dictionary<string, string> sections)
    {
        Fallback = fallback;
        _sections = sections;
    }

    public string Fallback { get; }

    public int SectionCount => _sections.Count;

    public static BulkChangelog ForAll(string? text) => new((text ?? string.Empty).Trim(), []);

    /// <summary>The same notes with another text for the mods without a section.</summary>
    public BulkChangelog WithFallback(string? text) => new((text ?? string.Empty).Trim(), _sections);

    /// <summary>
    /// Splits a file into sections. Markdown bodies are converted to the BBCode Steam renders;
    /// plain text is sent as written.
    /// </summary>
    public static BulkChangelog Parse(string content, bool markdown)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? heading = null;
        var body = new List<string>();
        var fallback = string.Empty;

        void Flush()
        {
            var text = string.Join('\n', body).Trim();
            if (markdown && text.Length > 0) text = MarkdownToBbCode.Convert(text).Trim();
            if (heading is null) fallback = text;
            else sections[heading] = text;
            body.Clear();
        }

        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            if (TryReadHeading(raw, out var title))
            {
                Flush();
                heading = title;
            }
            else
            {
                body.Add(raw);
            }
        }

        Flush();
        return new BulkChangelog(fallback, sections);
    }

    /// <summary>Notes for one mod: its section by id, then by title, else the fallback text.</summary>
    public string For(WorkshopItem item)
    {
        if (_sections.TryGetValue(item.Id.ToString(CultureInfo.InvariantCulture), out var byId)) return byId;
        return _sections.TryGetValue(item.Title.Trim(), out var byTitle) ? byTitle : Fallback;
    }

    public bool HasSectionFor(WorkshopItem item) =>
        _sections.ContainsKey(item.Id.ToString(CultureInfo.InvariantCulture)) || _sections.ContainsKey(item.Title.Trim());

    /// <summary>Headings that match none of the given mods: usually a typo in a title.</summary>
    public IReadOnlyList<string> SectionsWithoutMod(IEnumerable<WorkshopItem> items)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            known.Add(item.Id.ToString(CultureInfo.InvariantCulture));
            known.Add(item.Title.Trim());
        }

        return _sections.Keys.Where(k => !known.Contains(k)).ToList();
    }

    /// <summary>Mods whose notes exceed <see cref="MaxNoteLength"/>.</summary>
    public IReadOnlyList<WorkshopItem> TooLongFor(IEnumerable<WorkshopItem> items) =>
        items.Where(i => For(i).Length > MaxNoteLength).ToList();

    // "# Title" only: deeper headings ("## Fixes") stay part of the notes.
    private static bool TryReadHeading(string line, out string title)
    {
        title = string.Empty;
        if (!line.StartsWith("# ", StringComparison.Ordinal)) return false;

        title = line[2..].Trim().TrimEnd('#').Trim();
        return title.Length > 0;
    }
}
