using System;
using System.Collections.Generic;

namespace SteamWorkshopManager.Models;

/// <summary>What a drop zone accepts. Flags so one zone can take several kinds.</summary>
[Flags]
public enum DropKinds
{
    None = 0,
    Folder = 1,
    Images = 2,
}

/// <summary>
/// Result of a validated drop handed to a view model command. UI-agnostic so
/// view models never reference the Behaviors namespace.
/// </summary>
public sealed record DropPayload(DropKinds Kind, IReadOnlyList<string> Paths)
{
    public string? FirstPath => Paths.Count > 0 ? Paths[0] : null;
}
