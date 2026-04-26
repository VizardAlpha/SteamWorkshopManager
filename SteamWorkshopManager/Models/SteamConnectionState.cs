namespace SteamWorkshopManager.Models;

/// <summary>
/// Coarse Steam API connection state surfaced to the UI (status bar / footer).
/// </summary>
public enum SteamConnectionState
{
    Connecting,
    Connected,
    Disconnected,
}
