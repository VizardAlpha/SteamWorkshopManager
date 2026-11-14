namespace WorkshopManager.Models;

/// <summary>
/// When the app lets go of Steam. While the worker runs, Steam shows the user as playing the game;
/// on standby it is stopped and the next Steam call starts it again.
/// </summary>
public enum SteamIdleMode
{
    StayConnected,
    AfterEachAction,
    AfterInactivity,
}
