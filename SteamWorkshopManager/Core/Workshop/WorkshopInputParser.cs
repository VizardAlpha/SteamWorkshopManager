using System.Text.RegularExpressions;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>
/// Parses user-entered Workshop item references and YouTube links into ids.
/// Pure (no UI dependency) so it's shared by the create/edit flows and can be
/// unit-tested in isolation.
/// </summary>
public static class WorkshopInputParser
{
    /// <summary>
    /// Workshop item id from a raw numeric id or a "...?id=" / "...&amp;id=" URL.
    /// Returns 0 when nothing usable is found.
    /// </summary>
    public static ulong ParseWorkshopId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return 0;

        input = input.Trim();

        // Try as raw numeric ID
        if (ulong.TryParse(input, out var rawId))
            return rawId;

        // Try to extract ?id= from URL
        var match = Regex.Match(input, @"[?&]id=(\d+)");
        if (match.Success && ulong.TryParse(match.Groups[1].Value, out var urlId))
            return urlId;

        return 0;
    }

    /// <summary>
    /// YouTube video id from a raw 11-char id or watch/short/embed URL forms.
    /// Returns null when nothing usable is found.
    /// </summary>
    public static string? ParseYouTubeId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        if (Regex.IsMatch(input, @"^[A-Za-z0-9_-]{11}$"))
            return input;

        var v = Regex.Match(input, @"[?&]v=([A-Za-z0-9_-]{11})");
        if (v.Success) return v.Groups[1].Value;

        var shortLink = Regex.Match(input, @"youtu\.be/([A-Za-z0-9_-]{11})");
        if (shortLink.Success) return shortLink.Groups[1].Value;

        var embed = Regex.Match(input, @"youtube\.com/embed/([A-Za-z0-9_-]{11})");
        if (embed.Success) return embed.Groups[1].Value;

        return null;
    }
}