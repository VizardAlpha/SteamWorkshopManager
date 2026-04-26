namespace SteamWorkshopManager.Models;

/// <summary>
/// Top-level categories inside the Settings view. The view's left nav switches
/// the right-hand content pane based on the currently selected category.
/// </summary>
public enum SettingsCategory
{
    General,
    Privacy,
    Debug,
    Updates,
    About,
}