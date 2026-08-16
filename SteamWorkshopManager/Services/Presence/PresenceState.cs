using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services.Presence;

/// <summary>Snapshot of what the user is doing, mapped to a Discord activity.</summary>
public sealed record PresenceState(
    ShellTab Tab,
    string? GameName,
    uint AppId,
    string? ItemTitle,
    bool IsUploading)
{
    public static readonly PresenceState Idle = new(ShellTab.Home, null, 0, null, false);
}
