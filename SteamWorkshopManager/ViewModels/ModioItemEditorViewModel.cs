using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Platforms.Modio;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.ViewModels.Modio;

namespace SteamWorkshopManager.ViewModels;

public enum ModioEditorSection { Details, File, Media, Dependencies }

/// <summary>
/// Editor of a mod.io mod, in the sections of mod.io's own pages. One save sends what changed
/// in any section; tags the game's admins set (outside the catalog) are never removed.
/// </summary>
public partial class ModioItemEditorViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<ModioItemEditorViewModel>();

    private readonly WorkshopItem _item;
    private readonly ModioPublisher _publisher;
    private readonly ISessionContext _context;
    private readonly ThumbnailCache _thumbnails;
    private readonly IProgress<UploadProgress>? _uploadProgress;

    // Last values mod.io holds, moved forward after each save.
    private string _savedTitle;
    private string _savedSummary;
    private string _savedDescription;
    private string _savedHomepage = string.Empty;
    private int _savedCommunity = ModioCommunity.Default;
    private VisibilityType _savedVisibility;
    private HashSet<string> _savedTags;
    private string? _profileUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsActive), nameof(IsFileActive), nameof(IsMediaActive), nameof(IsDependenciesActive))]
    private ModioEditorSection _activeSection = ModioEditorSection.Details;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    /// <summary>Gallery, homepage and community options come from a full read of the mod.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLoadingMod = true;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteConfirmed))]
    private bool _showDeleteConfirmation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteConfirmed))]
    private string _deleteTypedConfirmation = string.Empty;

    public bool IsDeleteConfirmed =>
        ShowDeleteConfirmation && string.Equals(DeleteTypedConfirmation, DangerousActions.ConfirmationPassphrase, StringComparison.Ordinal);

    public ModioDetailsViewModel Details { get; }
    public ModioMediaViewModel Media { get; }
    public ModioFileViewModel File { get; }
    public ModioDependenciesViewModel Dependencies { get; }

    public bool IsDetailsActive => ActiveSection == ModioEditorSection.Details;
    public bool IsFileActive => ActiveSection == ModioEditorSection.File;
    public bool IsMediaActive => ActiveSection == ModioEditorSection.Media;
    public bool IsDependenciesActive => ActiveSection == ModioEditorSection.Dependencies;

    [RelayCommand] private void NavigateToDetails() => ActiveSection = ModioEditorSection.Details;
    [RelayCommand] private void NavigateToFile() => ActiveSection = ModioEditorSection.File;
    [RelayCommand] private void NavigateToMedia() => ActiveSection = ModioEditorSection.Media;
    [RelayCommand] private void NavigateToDependencies() => ActiveSection = ModioEditorSection.Dependencies;

    public ulong ItemId => _item.Id;

    public bool HasProfileUrl => !string.IsNullOrEmpty(_profileUrl);

    /// <summary>Shown from the cache, mod.io not checked yet: saving could overwrite a newer version.</summary>
    public bool IsCheckingPlatform => _item.IsRefreshing;

    public event Action<ulong>? ItemUpdated;
    public event Action? ItemDeleted;

    public ModioItemEditorViewModel(
        WorkshopItem item,
        ModioPublisher publisher,
        ISessionContext context,
        ThumbnailCache thumbnails,
        ModioDetailsViewModel details,
        ModioMediaViewModel media,
        ModioFileViewModel file,
        ModioDependenciesViewModel dependencies,
        IProgress<UploadProgress>? uploadProgress = null)
    {
        _item = item;
        _publisher = publisher;
        _context = context;
        _thumbnails = thumbnails;
        _uploadProgress = uploadProgress;
        _profileUrl = item.ProfileUrl;
        Details = details;
        Media = media;
        File = file;
        Dependencies = dependencies;

        Details.Title = _savedTitle = item.Title;
        Details.Summary = _savedSummary = item.Summary ?? string.Empty;
        Details.Description = _savedDescription = item.Description;
        Details.Visibility = _savedVisibility = item.Visibility;
        _savedTags = item.Tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Details.Tags.LoadFromSession(_savedTags);

        File.AttachTo(item.Id, item.Title, item.Revision);
        File.LiveFileChanged += OnLiveFileChanged;

        _item.PropertyChanged += OnItemPropertyChanged;
        if (item.PreviewImageUrl is { } url) _ = Details.LoadRemoteLogoAsync(thumbnails, url);
        _ = LoadModAsync();
    }

    private async Task LoadModAsync()
    {
        IsLoadingMod = true;
        try
        {
            ApplyMod(await _publisher.GetModAsync(_item.Id, _item.Revision));
            await File.LoadHistoryAsync();
            await Dependencies.LoadAsync(_item.Id, _item.Revision, _savedCommunity);
        }
        catch (Exception ex)
        {
            Log.Warning($"mod.io mod {_item.Id} could not be read: {ex.Message}");
            ErrorMessage = ex is ModioApiException api ? ModioErrors.Describe(api, "ModioLoadFailed") : Loc["ModioLoadFailed"];
        }
        finally
        {
            IsLoadingMod = false;
        }
    }

    /// <summary>Baseline for what the list doesn't carry.</summary>
    private void ApplyMod(ModioMod mod)
    {
        _savedHomepage = mod.HomepageUrl ?? string.Empty;
        _savedCommunity = mod.CommunityOptions;
        _profileUrl = mod.ProfileUrl ?? _profileUrl;
        Details.HomepageUrl = _savedHomepage;
        Details.LoadCommunityOptions(mod.CommunityOptions);
        Dependencies.AllowDependents = (mod.CommunityOptions & ModioCommunity.Dependencies) != 0;
        Media.LoadExisting(mod.Media, _thumbnails);
        File.CurrentFile = mod.Modfile;
        OnPropertyChanged(nameof(HasProfileUrl));
    }

    private void OnLiveFileChanged() => ItemUpdated?.Invoke(_item.Id);

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkshopItem.IsRefreshing)) return;
        OnPropertyChanged(nameof(IsCheckingPlatform));
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenOnModio()
    {
        if (!HasProfileUrl) return;
        try { Process.Start(new ProcessStartInfo(_profileUrl!) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Debug($"Failed to open the mod page: {ex.Message}"); }
    }

    private bool CanSave() => !IsSaving && !IsLoadingMod && !IsCheckingPlatform;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var d = Details;
        if (d.Validate() is { } errorKey)
        {
            ErrorMessage = Loc[errorKey];
            ActiveSection = ModioEditorSection.Details;
            return;
        }

        var catalog = (_context.Current?.TagsByCategory.Values.SelectMany(v => v) ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = d.Tags.SelectedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var homepage = d.HomepageUrl.Trim();
        var community = Dependencies.ApplyTo(d.CommunityOptions(_savedCommunity));

        var update = new ModioModUpdate(
            _item.Id,
            new ModioModEdit(
                Name: d.Title != _savedTitle ? d.Title.Trim() : null,
                Summary: d.Summary != _savedSummary ? d.Summary.Trim() : null,
                Description: d.Description != _savedDescription ? d.Description : null,
                Visible: d.Visibility != _savedVisibility ? d.IsPublic : null,
                HomepageUrl: homepage != _savedHomepage ? homepage : null,
                CommunityOptions: community != _savedCommunity ? community : null),
            TagsToAdd: selected.Except(_savedTags, StringComparer.OrdinalIgnoreCase).ToList(),
            // Only catalog tags can be unticked here; admin-set ones stay.
            TagsToRemove: _savedTags.Where(t => catalog.Contains(t) && !selected.Contains(t)).ToList(),
            d.LogoPath,
            Media.BuildChange(),
            File.ToUpload());

        IsSaving = true;
        ErrorMessage = null;
        try
        {
            await _publisher.UpdateAsync(update, _uploadProgress);

            _savedTitle = d.Title;
            _savedSummary = d.Summary;
            _savedDescription = d.Description;
            _savedVisibility = d.Visibility;
            _savedTags = selected;
            d.ClearPendingLogo();
            File.Clear();

            ItemUpdated?.Invoke(_item.Id);
            // New gallery file names and the new live file only exist on mod.io's side.
            ApplyMod(await _publisher.GetModAsync(_item.Id));
            if (update.File is not null) await File.LoadHistoryAsync();
        }
        catch (ModioApiException ex)
        {
            ErrorMessage = ModioErrors.Describe(ex, "UpdateFailed");
        }
        catch (Exception ex)
        {
            Log.Error("mod.io save failed", ex);
            ErrorMessage = $"{Loc["UpdateFailed"]}: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void ShowDeleteDialog()
    {
        DeleteTypedConfirmation = string.Empty;
        ShowDeleteConfirmation = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirmation = false;
        DeleteTypedConfirmation = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (!IsDeleteConfirmed) return;

        IsDeleting = true;
        ErrorMessage = null;
        try
        {
            await _publisher.DeleteAsync(_item.Id);
            ItemDeleted?.Invoke();
        }
        catch (ModioApiException ex)
        {
            ErrorMessage = ModioErrors.Describe(ex, "DeleteFailed");
            ShowDeleteConfirmation = false;
        }
        finally
        {
            IsDeleting = false;
        }
    }

    public void Dispose()
    {
        _item.PropertyChanged -= OnItemPropertyChanged;
        File.LiveFileChanged -= OnLiveFileChanged;
        Details.Dispose();
        Media.Dispose();
        File.Dispose();
        Dependencies.Dispose();
    }
}

/// <summary>User-facing text for a failed mod.io call.</summary>
internal static class ModioErrors
{
    /// <summary>Field errors from mod.io are the most useful part: they name what to fix.</summary>
    public static string Describe(ModioApiException ex, string fallbackKey)
    {
        var loc = ViewModelBase.Loc;
        if (ex.IsAuthError) return loc["ModioTokenRejected"];
        if (ex.IsRateLimited) return loc["ModioRateLimited"];
        var detail = ex.FieldErrors is { Count: > 0 } fields ? string.Join(" ", fields.Values) : ex.Message;
        return $"{loc[fallbackKey]}: {detail}";
    }
}
