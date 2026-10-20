using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Workshop;
using Steamworks;

namespace SteamWorkshopManager.ViewModels.Editor;

/// <summary>
/// Per-language titles and descriptions. The base (english) text stays on the Info
/// section; this view-model only holds the other languages. Staged mode (create view)
/// only edits the texts; once <see cref="AttachTo"/> binds a published item, it reads
/// what Steam holds. Results live in memory, nothing is cached on disk.
/// </summary>
public partial class TranslationEditorViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<TranslationEditorViewModel>();

    private readonly ItemTranslationService _translations;
    private readonly CancellationTokenSource _cts = new();
    private PublishedFileId_t _owner;
    private Func<ItemText> _currentBase = () => new ItemText("", "");
    private ItemText? _steamBaseFull;

    public TranslationEditorViewModel(ItemTranslationService translations)
    {
        _translations = translations;
        Entries.CollectionChanged += OnEntriesChanged;
        foreach (var language in SteamLanguages.Translatable) AvailableLanguages.Add(language);
    }

    public SteamLanguage BaseLanguage => SteamLanguages.Base;

    /// <summary>Languages shown in the list, in <see cref="SteamLanguages.All"/> order.</summary>
    public ObservableCollection<TranslationEntryViewModel> Entries { get; } = [];

    /// <summary>Languages not in <see cref="Entries"/> yet, offered by the "Add a language" picker.</summary>
    public ObservableCollection<SteamLanguage> AvailableLanguages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private TranslationEntryViewModel? _selected;

    [ObservableProperty]
    private SteamLanguage? _languageToAdd;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private int _scannedCount;

    [ObservableProperty]
    private string? _error;

    public int ScanTotal => SteamLanguages.Translatable.Count;

    public bool IsLoaded { get; private set; }

    public bool HasSelection => Selected is not null;

    private bool IsAttached => _owner != PublishedFileId_t.Invalid;

    /// <summary>Languages on Steam; in staged mode, the languages with text to send.</summary>
    public int TranslatedCount => Entries.Count(e => IsAttached ? e.ExistsOnSteam : e.IsDirty);

    public bool HasTranslations => TranslatedCount > 0;

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Binds to an item. <paramref name="currentBase"/> reads the Info section's title and description.</summary>
    public void AttachTo(PublishedFileId_t owner, Func<ItemText> currentBase)
    {
        _owner = owner;
        _currentBase = currentBase;
    }

    /// <summary>Staged mode: <paramref name="currentBase"/> reads the Info section's title and description.</summary>
    public void UseBase(Func<ItemText> currentBase) => _currentBase = currentBase;

    /// <summary>Staged texts, for a create draft.</summary>
    public List<DraftTranslation> GetStaged() =>
        Entries.Where(e => e.IsDirty)
            .Select(e => new DraftTranslation(e.Language.ApiCode, e.Title, e.Description))
            .ToList();

    /// <summary>Replaces the staged languages with a draft's.</summary>
    public void RestoreStaged(IEnumerable<DraftTranslation>? staged)
    {
        Selected = null;
        foreach (var entry in Entries.ToList()) Remove(entry);
        foreach (var draft in staged ?? [])
        {
            if (SteamLanguages.Find(draft.Language) is not { } language || FindEntry(language) is not null) continue;
            var entry = AddEntry(language);
            entry.Title = draft.Title;
            entry.Description = draft.Description;
        }
    }

    /// <summary>
    /// Finds the languages already translated on Steam, one query at a time. Full
    /// descriptions: translations often share the title and the first 255 bytes with the base.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoaded || IsScanning) return;

        IsScanning = true;
        ScannedCount = 0;
        Error = null;
        var unanswered = 0;
        try
        {
            var baseText = await GetSteamBaseFullAsync();
            if (baseText is null)
            {
                Error = Loc["TranslationsLoadFailed"];
                return;
            }

            foreach (var language in SteamLanguages.Translatable)
            {
                if (_cts.IsCancellationRequested) return;
                if (FindEntry(language) is null)
                {
                    var text = await _translations.GetTextAsync(_owner, language.ApiCode, fullDescription: true);
                    if (text is null) unanswered++;
                    else if (ItemTranslations.IsTranslated(baseText, text) && FindEntry(language) is null)
                    {
                        var entry = AddEntry(language);
                        entry.ApplySteamText(text, fullyLoaded: true);
                    }
                }
                ScannedCount++;
            }

            IsLoaded = true;
            if (unanswered > 0) Error = string.Format(Loc["TranslationsPartiallyLoaded"], unanswered);
        }
        catch (Exception ex)
        {
            Log.Warning($"Translation scan failed: {ex.Message}");
            Error = Loc["TranslationsLoadFailed"];
        }
        finally
        {
            IsScanning = false;
        }
    }

    partial void OnSelectedChanged(TranslationEntryViewModel? value)
    {
        if (value is { ExistsOnSteam: true, IsFullyLoaded: false, IsLoading: false })
            _ = LoadFullTextAsync(value);
    }

    partial void OnLanguageToAddChanged(SteamLanguage? value)
    {
        if (value is null) return;

        // Removing the selected item from the ComboBox source makes it select the next one:
        // clear the selection first, outside the ComboBox's own selection change.
        Dispatcher.UIThread.Post(() =>
        {
            LanguageToAdd = null;
            if (FindEntry(value) is { } existing)
            {
                Selected = existing;
                return;
            }

            var entry = AddEntry(value);
            Selected = entry;
            // The scan may still be running or have skipped this language: read it from Steam.
            if (IsAttached) _ = LoadFullTextAsync(entry);
        });
    }

    private async Task LoadFullTextAsync(TranslationEntryViewModel entry)
    {
        entry.IsLoading = true;
        try
        {
            var baseText = await GetSteamBaseFullAsync();
            var text = await _translations.GetTextAsync(_owner, entry.Language.ApiCode, fullDescription: true);
            if (baseText is null || text is null)
            {
                Error = Loc["TranslationLoadFailed"];
                return;
            }

            // A row the user already typed into keeps their text.
            if (entry.ExistsOnSteam || !entry.IsDirty)
                entry.ApplySteamText(ItemTranslations.IsTranslated(baseText, text) ? text : null, fullyLoaded: true);
            OnPropertyChanged(nameof(TranslatedCount));
            OnPropertyChanged(nameof(HasTranslations));
        }
        catch (Exception ex)
        {
            Log.Warning($"Loading {entry.Language.ApiCode} text failed: {ex.Message}");
            Error = Loc["TranslationLoadFailed"];
        }
        finally
        {
            entry.IsLoading = false;
        }
    }

    private async Task<ItemText?> GetSteamBaseFullAsync() =>
        _steamBaseFull ??= await _translations.GetTextAsync(_owner, SteamLanguages.BaseCode, fullDescription: true);

    [RelayCommand]
    private void CopyBase()
    {
        if (Selected is not { IsEditable: true } entry) return;
        var baseText = _currentBase();
        entry.Title = baseText.Title;
        entry.Description = baseText.Description;
    }

    /// <summary>Drops a language that was never sent. Steam has no call to delete a translation.</summary>
    [RelayCommand]
    private void Remove(TranslationEntryViewModel? entry)
    {
        entry ??= Selected;
        if (entry is null || entry.ExistsOnSteam) return;

        Entries.Remove(entry);
        InsertSorted(AvailableLanguages, entry.Language, l => l);
        if (Selected == entry) Selected = null;
    }

    /// <summary>Changes to send after the main update, resolved against the base text being saved.</summary>
    public IReadOnlyList<TranslationChange> GetPendingChanges(ItemText baseText) =>
        Entries
            .Where(e => e.IsEditable)
            .Select(e => ItemTranslations.PlanChange(e.Language.ApiCode, e.SteamText, e.Edited, baseText))
            .OfType<TranslationChange>()
            .ToList();

    /// <summary>Takes the sent texts as the new Steam state.</summary>
    public void ApplySaved(IReadOnlyList<TranslationChange> sent, IReadOnlyCollection<string> failed)
    {
        _steamBaseFull = null;
        foreach (var change in sent.Where(c => !failed.Contains(c.Language)))
        {
            if (SteamLanguages.Find(change.Language) is { } language && FindEntry(language) is { } entry)
                entry.ApplySteamText(change.Text, fullyLoaded: true);
        }
    }

    private TranslationEntryViewModel? FindEntry(SteamLanguage language) =>
        Entries.FirstOrDefault(e => e.Language == language);

    private TranslationEntryViewModel AddEntry(SteamLanguage language)
    {
        var entry = new TranslationEntryViewModel(language);
        InsertSorted(Entries, entry, e => e.Language);
        AvailableLanguages.Remove(language);
        return entry;
    }

    private static void InsertSorted<T>(ObservableCollection<T> list, T item, Func<T, SteamLanguage> languageOf)
    {
        var order = OrderOf(languageOf(item));
        var index = 0;
        while (index < list.Count && OrderOf(languageOf(list[index])) < order) index++;
        list.Insert(index, item);
    }

    private static int OrderOf(SteamLanguage language) => SteamLanguages.All.TakeWhile(l => l != language).Count();

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (TranslationEntryViewModel entry in e.NewItems) entry.PropertyChanged += OnEntryChanged;
        if (e.OldItems != null)
            foreach (TranslationEntryViewModel entry in e.OldItems) entry.PropertyChanged -= OnEntryChanged;

        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(TranslatedCount));
        OnPropertyChanged(nameof(HasTranslations));
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TranslationEntryViewModel.SteamText) or nameof(TranslationEntryViewModel.IsDirty))
        {
            OnPropertyChanged(nameof(TranslatedCount));
            OnPropertyChanged(nameof(HasTranslations));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
