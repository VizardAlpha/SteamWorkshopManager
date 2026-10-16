using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>Rules for Workshop item translations, kept free of Steam and UI code.</summary>
public static class ItemTranslations
{
    /// <summary>
    /// Steam answers with the base text when a language was never translated. Empty texts
    /// are a translation too: Steam shows them as a blank title and description.
    /// </summary>
    public static bool IsTranslated(ItemText baseText, ItemText candidate) =>
        candidate.Title != baseText.Title || candidate.Description != baseText.Description;

    public static bool IsEmpty(ItemText text) =>
        string.IsNullOrWhiteSpace(text.Title) && string.IsNullOrWhiteSpace(text.Description);

    /// <summary>A field left empty falls back to the base text, so a title-only translation keeps the base description.</summary>
    public static ItemText ResolveForSubmit(ItemText edited, ItemText baseText) => new(
        string.IsNullOrWhiteSpace(edited.Title) ? baseText.Title : edited.Title,
        string.IsNullOrWhiteSpace(edited.Description) ? baseText.Description : edited.Description);

    /// <summary>
    /// What to send for one language, or null when nothing changed. Steam cannot delete a
    /// translation and shows empty texts as blanks, so empty fields are always replaced by the base.
    /// </summary>
    public static TranslationChange? PlanChange(string language, ItemText? steamText, ItemText edited, ItemText baseText)
    {
        if (steamText is null && IsEmpty(edited)) return null;
        if (steamText is not null && edited == steamText) return null;
        return new TranslationChange(language, ResolveForSubmit(edited, baseText));
    }
}
