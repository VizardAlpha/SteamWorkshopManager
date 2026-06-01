namespace SteamWorkshopManager.Core.Workshop;

/// <summary>
/// Media formats Steam accepts for Workshop preview images. Single source of
/// truth shared by the file pickers and drag-and-drop filtering.
/// </summary>
public static class WorkshopMedia
{
    public static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp"];
}
