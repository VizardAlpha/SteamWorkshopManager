using System;

namespace SteamWorkshopManager.Models;

public class ItemVersion
{
    public required string VersionName { get; init; }
    public required string GameVersionCompatibility { get; init; }
    public bool IsCompatibleWithAllVersions { get; init; }
    public DateTime CreatedAt { get; init; }
}
