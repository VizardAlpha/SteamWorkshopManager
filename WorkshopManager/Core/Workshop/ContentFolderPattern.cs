using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WorkshopManager.Models;

namespace WorkshopManager.Core.Workshop;

/// <summary>
/// Finds the content folder of mods the app has no folder for, from a path the user describes
/// once, such as <c>C:\Mods\{name}</c>. <c>{name}</c> stands for the mod's title and
/// <c>{id}</c> for its id. Folder names are compared without case, spaces or punctuation, so
/// "Better Storage (v2)" finds "BetterStorage_v2", on case-sensitive file systems too.
/// </summary>
public static class ContentFolderPattern
{
    public const string Name = "{name}";
    public const string Id = "{id}";

    /// <summary>An absolute path with at least one placeholder below its first segment.</summary>
    public static bool IsValid(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        var segments = Split(pattern);
        var first = Array.FindIndex(segments, HasPlaceholder);
        return first > 0 && Path.IsPathRooted(ToNativePath(pattern));
    }

    /// <summary>Letters and digits only, lower-cased: what two names must share to match.</summary>
    public static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The folder each mod resolves to; a mod is absent when nothing matches or when several
    /// folders match it, since guessing between them could upload the wrong content.
    /// </summary>
    public static IReadOnlyDictionary<ulong, string> Resolve(string pattern, IReadOnlyList<WorkshopItem> items)
    {
        var found = new Dictionary<ulong, string>();
        if (!IsValid(pattern)) return found;

        var segments = Split(pattern);
        var first = Array.FindIndex(segments, HasPlaceholder);
        var root = BuildRoot(pattern, segments, first);
        if (!Directory.Exists(root)) return found;

        // One listing of the root for every mod: with 150 mods this is the costly part.
        var byKey = SafeSubfolders(root).ToLookup(Normalize, StringComparer.Ordinal);

        foreach (var item in items)
        {
            var key = Normalize(Fill(segments[first], item));
            if (key.Length == 0) continue;

            var matches = byKey[key].ToList();
            if (matches.Count != 1) continue;

            var folder = Path.Combine(root, matches[0]);
            for (var i = first + 1; i < segments.Length && folder is not null; i++)
                folder = FindChild(folder, Fill(segments[i], item));

            if (folder is not null) found[item.Id] = folder;
        }

        return found;
    }

    private static bool HasPlaceholder(string segment) =>
        segment.Contains(Name, StringComparison.OrdinalIgnoreCase) || segment.Contains(Id, StringComparison.OrdinalIgnoreCase);

    private static string Fill(string segment, WorkshopItem item) => segment
        .Replace(Name, item.Title, StringComparison.OrdinalIgnoreCase)
        .Replace(Id, item.Id.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

    // Both separators are accepted whatever the OS: a pattern is often pasted from somewhere else.
    private static string[] Split(string pattern) =>
        pattern.Trim().Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static string ToNativePath(string pattern) =>
        pattern.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static string BuildRoot(string pattern, string[] segments, int count)
    {
        var root = string.Join(Path.DirectorySeparatorChar, segments.Take(count));
        // Splitting dropped the leading separator of a Unix path, and "C:" alone means "current folder on C".
        if (ToNativePath(pattern).StartsWith(Path.DirectorySeparatorChar)) return Path.DirectorySeparatorChar + root;
        return root.EndsWith(':') ? root + Path.DirectorySeparatorChar : root;
    }

    /// <summary>The child folder with that name, whatever its case.</summary>
    private static string? FindChild(string parent, string name)
    {
        var exact = Path.Combine(parent, name);
        if (Directory.Exists(exact)) return exact;

        var match = SafeSubfolders(parent).Where(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return match.Count == 1 ? Path.Combine(parent, match[0]) : null;
    }

    private static IEnumerable<string> SafeSubfolders(string parent)
    {
        try
        {
            return Directory.EnumerateDirectories(parent).Select(d => Path.GetFileName(d)!).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
