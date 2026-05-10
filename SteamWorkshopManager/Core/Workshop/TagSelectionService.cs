using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Session;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>
/// Tag-selection logic shared between the Create and Edit flows. Persists
/// custom-tag changes to the active session via <see cref="ISessionRepository"/>
/// so the VMs no longer need to chase the service locator.
/// </summary>
public sealed class TagSelectionService(ISessionRepository sessionRepository)
{
    /// <summary>Adds a custom tag to the active session if not already present
    /// (case-insensitive). Fire-and-forget persist — failures are ignored.</summary>
    public void AddCustomTagToSession(string tagName)
    {
        var session = AppConfig.CurrentSession;
        if (session is null) return;
        if (session.CustomTags.Contains(tagName, StringComparer.OrdinalIgnoreCase)) return;

        session.CustomTags.Add(tagName);
        PersistFireAndForget(session);
    }

    public void RemoveCustomTagFromSession(string tagName)
    {
        var session = AppConfig.CurrentSession;
        if (session is null) return;

        var index = session.CustomTags.FindIndex(t =>
            t.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        session.CustomTags.RemoveAt(index);
        PersistFireAndForget(session);
    }

    /// <summary>
    /// Reapplies a draft's tag selection over the current tag model: merges
    /// custom-tag names, restores IsSelected per-tag, and promotes any
    /// selected name not in either source to a new custom tag (covers Steam
    /// category reshuffles between the draft save and reload).
    /// </summary>
    public static void RestoreFromDraft(
        CreateDraft draft,
        ObservableCollection<TagCategory> tagCategories,
        ObservableCollection<WorkshopTag> customTags)
    {
        foreach (var name in draft.CustomTags ?? [])
        {
            if (!customTags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                customTags.Add(new WorkshopTag(name));
        }

        var selected = new HashSet<string>(draft.SelectedTags ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tagCategories.SelectMany(c => c.Tags))
            tag.IsSelected = selected.Contains(tag.Name);
        foreach (var tag in customTags)
            tag.IsSelected = selected.Contains(tag.Name);

        var knownNames = new HashSet<string>(
            tagCategories.SelectMany(c => c.Tags).Select(t => t.Name)
                .Concat(customTags.Select(t => t.Name)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in selected.Where(n => !knownNames.Contains(n)))
            customTags.Add(new WorkshopTag(name, isSelected: true));
    }

    /// <summary>Flat list of currently selected tag names across category and
    /// custom collections, deduped case-insensitively. Used at submit time
    /// and for draft persistence.</summary>
    public static List<string> CollectSelectedNames(
        IEnumerable<TagCategory> tagCategories,
        IEnumerable<WorkshopTag> customTags) =>
        tagCategories.SelectMany(c => c.Tags)
            .Where(t => t.IsSelected)
            .Select(t => t.Name)
            .Concat(customTags.Where(t => t.IsSelected).Select(t => t.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async void PersistFireAndForget(WorkshopSession session)
    {
        try { await sessionRepository.SaveSessionAsync(session); }
        catch { /* fire-and-forget */ }
    }
}
