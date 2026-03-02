namespace SteamWorkshopManager.Models;

/// <summary>
/// Represents a Steam game branch (beta) for version compatibility.
/// </summary>
public class GameBranch
{
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public uint BuildId { get; init; }
    public uint Flags { get; init; }

    public bool IsDefault => (Flags & 1) != 0;
    public bool IsAvailable => (Flags & 2) != 0;
    public bool IsPrivate => (Flags & 4) != 0;

    public string DisplayName => string.IsNullOrEmpty(Description) || Description == Name
        ? Name : $"{Name} \u2014 {Description}";
}
