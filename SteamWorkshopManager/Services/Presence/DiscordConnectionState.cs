namespace SteamWorkshopManager.Services.Presence;

/// <summary>
/// Live state of the IPC link to the Discord client. Distinct from
/// <see cref="Models.DiscordPresenceMode"/>, which is the saved preference:
/// the mode says what the user wants published, this says whether any of it
/// is actually getting through.
/// </summary>
public enum DiscordConnectionState
{
    /// <summary>Presence is off, nothing is attempted.</summary>
    Disabled,

    /// <summary>Waiting for the Discord client to answer on the IPC pipe.</summary>
    Connecting,

    /// <summary>Handshake done, the activity is live.</summary>
    Connected,

    /// <summary>Given up after repeated failures. Only a manual retry resumes.</summary>
    Unreachable,
}