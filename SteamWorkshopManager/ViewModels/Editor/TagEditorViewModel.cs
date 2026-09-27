using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.ViewModels.Editor;

/// <summary>Tag picker shared by the create and edit views: session catalog, custom tags, refresh.</summary>
public partial class TagEditorViewModel(
    ISessionContext context,
    TagSelectionService tagSelection,
    SessionManager sessionManager) : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<TagEditorViewModel>();

    public ObservableCollection<TagCategory> TagCategories { get; } = [];
    public ObservableCollection<WorkshopTag> CustomTags { get; } = [];

    [ObservableProperty]
    private string _newCustomTag = string.Empty;

    [ObservableProperty]
    private bool _isRefreshingTags;

    [ObservableProperty]
    private string _tagsLastUpdatedText = string.Empty;

    [ObservableProperty]
    private bool _hasTags;

    public List<string> SelectedNames => TagSelectionService.CollectSelectedNames(TagCategories, CustomTags);

    /// <summary>
    /// Rebuilds the picker from the active session. Names in <paramref name="selected"/> are ticked;
    /// when <paramref name="adoptUnknown"/> is set, selected names outside the catalog become custom tags
    /// (an existing item may carry tags the session doesn't know yet).
    /// </summary>
    public void LoadFromSession(IReadOnlyCollection<string>? selected = null, bool adoptUnknown = false)
    {
        var selectedSet = new HashSet<string>(selected ?? [], StringComparer.OrdinalIgnoreCase);
        var session = context.Current;

        BuildCategories(session?.TagsByCategory ?? new Dictionary<string, List<string>>(), session?.DropdownCategories ?? [],
            // Steam may store a tag with or without its category prefix.
            (category, tag) => selectedSet.Contains(tag) || selectedSet.Contains($"{category}: {tag}"));

        CustomTags.Clear();
        foreach (var customTag in session?.CustomTags ?? [])
            CustomTags.Add(new WorkshopTag(customTag, selectedSet.Contains(customTag)));

        if (adoptUnknown)
        {
            var known = (session?.TagsByCategory ?? new Dictionary<string, List<string>>())
                .SelectMany(kvp => kvp.Value.Concat(kvp.Value.Select(v => $"{kvp.Key}: {v}")))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var name in selectedSet)
            {
                if (known.Contains(name) || CustomTags.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                tagSelection.AddCustomTagToSession(name);
                CustomTags.Add(new WorkshopTag(name, true));
            }
        }

        UpdateTagsLastUpdatedText();
    }

    public void RestoreFromDraft(CreateDraft draft) =>
        TagSelectionService.RestoreFromDraft(draft, TagCategories, CustomTags);

    [RelayCommand]
    private async Task RefreshTagsAsync()
    {
        var session = context.Current;
        if (session == null) return;

        IsRefreshingTags = true;
        try
        {
            var selected = SelectedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            await sessionManager.RefreshTagsAsync(session);

            BuildCategories(session.TagsByCategory, session.DropdownCategories,
                (_, tag) => selected.Contains(tag));
            UpdateTagsLastUpdatedText();
        }
        catch (Exception ex)
        {
            Log.Warning($"Tag refresh failed: {ex.Message}");
        }
        finally
        {
            IsRefreshingTags = false;
        }
    }

    [RelayCommand]
    private void AddCustomTag()
    {
        if (string.IsNullOrWhiteSpace(NewCustomTag)) return;

        var tagName = NewCustomTag.Trim();
        NewCustomTag = string.Empty;
        if (CustomTags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))) return;

        tagSelection.AddCustomTagToSession(tagName);
        CustomTags.Add(new WorkshopTag(tagName, true));
    }

    [RelayCommand]
    private void RemoveCustomTag(WorkshopTag? tag)
    {
        if (tag == null) return;

        tagSelection.RemoveCustomTagFromSession(tag.Name);
        CustomTags.Remove(tag);
    }

    private void BuildCategories(
        Dictionary<string, List<string>> tagsByCategory,
        ICollection<string> dropdownCategories,
        Func<string, string, bool> isSelected)
    {
        TagCategories.Clear();
        foreach (var (category, tags) in tagsByCategory)
        {
            var tagCategory = new TagCategory
            {
                Name = category,
                IsDropdown = dropdownCategories.Contains(category),
            };
            foreach (var tag in tags)
                tagCategory.Tags.Add(new WorkshopTag(tag, isSelected(category, tag)));
            tagCategory.SyncSelectedTag();
            TagCategories.Add(tagCategory);
        }
        HasTags = TagCategories.Count > 0;
    }

    private void UpdateTagsLastUpdatedText()
    {
        var lastUpdated = context.Current?.TagsLastUpdated;
        TagsLastUpdatedText = lastUpdated is { Year: > 2000 }
            ? $"{LocalizationService.GetString("TagsUpdated")} {lastUpdated.Value.ToLocalTime():g}"
            : string.Empty;
    }
}
