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
}
