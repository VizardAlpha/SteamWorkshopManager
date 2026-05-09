namespace SteamWorkshopManager.Helpers;

/// <summary>
/// Shared constants for type-to-confirm destructive flows: deleting a mod,
/// bulk-deleting mods, deleting a session. The literal also doubles as the
/// TextBox placeholder so the user sees what to type — see the
/// <c>{x:Static}</c> bindings in the AXAML modals.
/// </summary>
public static class DangerousActions
{
    public const string ConfirmationPassphrase = "DELETE";
}
