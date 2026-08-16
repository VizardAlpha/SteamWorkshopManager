namespace SteamWorkshopManager.Services.Presence;

public interface IDiscordPresenceService
{
    /// <summary>
    /// Connects or disconnects to match the saved mode. Call once at startup
    /// and again whenever the user changes the setting.
    /// </summary>
    void Sync();

    /// <summary>Publishes the current activity. No-op while presence is off.</summary>
    void Update(PresenceState state);

    /// <summary>Clears the activity and drops the connection.</summary>
    void Shutdown();
}
