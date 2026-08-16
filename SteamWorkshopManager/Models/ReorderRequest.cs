namespace SteamWorkshopManager.Models;

/// <summary>
/// Payload of a drag reorder: <paramref name="Source"/> was dropped onto
/// <paramref name="Target"/>. Both are the dragged controls' DataContext, so
/// the receiving view model decides what the types mean.
/// </summary>
public sealed record ReorderRequest(object Source, object Target);