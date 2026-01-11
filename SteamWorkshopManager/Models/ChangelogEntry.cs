using System;

namespace SteamWorkshopManager.Models;

public class ChangelogEntry
{
    public required string Content { get; init; }
    public required DateTime Date { get; init; }
    public string? Version { get; init; }

    public string FormattedDate => Date.ToString("dd/MM/yyyy HH:mm");
}
