using System;

namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Display formatters shared by dashboards, list rows, and detail views so the
/// compact/relative representations stay consistent everywhere.
/// </summary>
public static class Formatters
{
    public static string CompactNumber(long value)
    {
        if (value <= 0) return "0";
        if (value < 1_000) return value.ToString();
        if (value < 1_000_000) return $"{value / 1_000.0:F1}K";
        if (value < 1_000_000_000) return $"{value / 1_000_000.0:F1}M";
        return $"{value / 1_000_000_000.0:F1}B";
    }

    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024L * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
        };
    }

    public static string TimeAgo(DateTime utc)
    {
        var diff = DateTime.UtcNow - utc;
        if (diff.TotalSeconds < 1) return "just now";
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}w ago";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}mo ago";
        return $"{(int)(diff.TotalDays / 365)}y ago";
    }
}
