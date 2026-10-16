namespace SteamWorkshopManager.Models;

/// <summary>Title and description of a Workshop item in one language.</summary>
public sealed record ItemText(string Title, string Description);

/// <summary>One translation to send. Never empty: Steam would show a blank title and description.</summary>
public sealed record TranslationChange(string Language, ItemText Text);
