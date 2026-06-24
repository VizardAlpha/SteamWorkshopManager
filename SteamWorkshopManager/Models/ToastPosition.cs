namespace SteamWorkshopManager.Models;

/// <summary>
/// Screen corner where transient toast notifications appear. Chosen by the user
/// in Settings → Customization and consumed by the shell's toast layer.
/// </summary>
public enum ToastPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}