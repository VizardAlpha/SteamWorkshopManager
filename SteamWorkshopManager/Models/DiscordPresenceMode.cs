namespace SteamWorkshopManager.Models;

/// <summary>
/// How much of the current activity is published to Discord. Off by default:
/// nothing reaches Discord until the user opts in, in the setup wizard or in
/// Settings > Customization.
/// </summary>
public enum DiscordPresenceMode
{
    /// <summary>No connection to Discord at all.</summary>
    Off,

    /// <summary>Generic activity only, no game and no item title.</summary>
    Minimal,

    /// <summary>Current section plus the game being modded.</summary>
    Game,

    /// <summary>Everything, including the title of the item being edited.</summary>
    Detailed,
}
