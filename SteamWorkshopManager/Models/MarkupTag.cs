namespace SteamWorkshopManager.Models;

/// <summary>A toolbar button of the markup editor: wraps the selection in <see cref="OpenTag"/> and <see cref="CloseTag"/>.</summary>
public record MarkupTag(string Label, string OpenTag, string CloseTag, string? Placeholder = null, string? Tooltip = null);
