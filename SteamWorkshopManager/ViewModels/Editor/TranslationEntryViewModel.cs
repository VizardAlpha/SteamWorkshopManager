using CommunityToolkit.Mvvm.ComponentModel;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.ViewModels.Editor;

/// <summary>One language row: the text being edited and what Steam currently holds.</summary>
public partial class TranslationEntryViewModel(SteamLanguage language) : ObservableObject
{
    public SteamLanguage Language { get; } = language;

    /// <summary>Text on Steam, null when this language has no translation there yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExistsOnSteam))]
    [NotifyPropertyChangedFor(nameof(IsEmptyOnSteam))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private ItemText? _steamText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _description = string.Empty;

    /// <summary>False until the full text is known: editing waits for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _isFullyLoaded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _isLoading;

    public bool ExistsOnSteam => SteamText is not null;

    /// <summary>A translation emptied on Steam: visitors in this language see a blank title and description.</summary>
    public bool IsEmptyOnSteam => SteamText is { } text && ItemTranslations.IsEmpty(text);

    public bool IsEditable => IsFullyLoaded && !IsLoading;

    public ItemText Edited => new(Title, Description);

    public bool IsDirty => SteamText is null ? !ItemTranslations.IsEmpty(Edited) : Edited != SteamText;

    /// <summary>Takes Steam's text as both the reference and the edited value.</summary>
    public void ApplySteamText(ItemText? text, bool fullyLoaded)
    {
        SteamText = text;
        Title = text?.Title ?? string.Empty;
        Description = text?.Description ?? string.Empty;
        IsFullyLoaded = fullyLoaded;
    }
}
