using System;

namespace SteamWorkshopManager.Services.Presence;

public interface IDiscordPresenceService
{
    /// <summary>Live state of the link to the Discord client.</summary>
    DiscordConnectionState ConnectionState { get; }

    /// <summary>
    /// Raised when <see cref="ConnectionState"/> changes. Fires from a
    /// background thread, so marshal before touching the UI.
    /// </summary>
    event Action? ConnectionStateChanged;

    /// <summary>
    /// Connects or disconnects to match the saved mode. Call once at startup,
    /// whenever the user changes the setting, and to retry after a give-up.
    /// </summary>
    void Sync();

    /// <summary>Publishes the current activity. No-op while presence is off.</summary>
    void Update(PresenceState state);

    /// <summary>Clears the activity and drops the connection.</summary>
    void Shutdown();
}