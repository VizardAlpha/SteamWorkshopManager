using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Telemetry;

namespace SteamWorkshopManager.ViewModels;

public partial class ItemListViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<ItemListViewModel>();
    private readonly ISteamService _steamService;
    private readonly INotificationService? _notifications;
    private static readonly HttpClient HttpClient = new();

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasNoItems = true;

    /// <summary>
    /// Free-text filter for the mod grid. Matches the item title
    /// case-insensitively; empty string means "no filter".
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// True when the user has entered multi-select mode via the toolbar.
    /// Cards swap their click handler from "open editor" to "toggle IsSelected"
    /// and the toolbar surfaces bulk visibility / delete actions.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotInSelectionMode))]
    private bool _selectionMode;

    public bool IsNotInSelectionMode => !SelectionMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(AllSelected))]
    private int _selectedCount;

    public bool HasSelection => SelectedCount > 0;
    public bool AllSelected => Items.Count > 0 && SelectedCount == Items.Count;

    [ObservableProperty]
    private bool _isBulkProcessing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBulkDeleteConfirmed))]
    private bool _showBulkDeleteConfirmation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBulkDeleteConfirmed))]
    private string _bulkDeleteTypedConfirmation = string.Empty;

    public bool IsBulkDeleteConfirmed =>
        string.Equals(BulkDeleteTypedConfirmation, DangerousActions.ConfirmationPassphrase, StringComparison.Ordinal);

    public ObservableCollection<WorkshopItem> Items { get; } = [];

    public ObservableCollection<WorkshopItem> FilteredItems { get; } = [];

    public event Action<WorkshopItem>? ItemSelected;
    public event Action? CreateRequested;

    public ItemListViewModel(ISteamService steamService, INotificationService? notifications = null)
    {
        _steamService = steamService;
        _notifications = notifications;
        Items.CollectionChanged += OnItemsChanged;
    }

    [RelayCommand]
    public async Task LoadItemsAsync()
    {
        if (!_steamService.IsInitialized)
        {
            ErrorMessage = Loc["SteamNotInitialized"];
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var items = await _steamService.GetPublishedItemsAsync();
            DisposeAndClearItems();
            foreach (var item in items)
            {
                Items.Add(item);
                _ = LoadItemPreviewAsync(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{Loc["LoadingError"]}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            HasNoItems = Items.Count == 0;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplyFilter();
        RecomputeSelectionState();
    }

    /// <summary>
    /// Releases each item's cached thumbnail before clearing the collection.
    /// ObservableCollection.Clear() only drops references; without explicit
    /// disposal the native Skia surfaces accumulate across session switches.
    /// </summary>
    public void DisposeAndClearItems()
    {
        foreach (var item in Items)
        {
            item.PreviewBitmap = null;
            item.PropertyChanged -= OnItemPropertyChanged;
        }
        Items.Clear();
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var q = SearchQuery?.Trim() ?? string.Empty;
        IEnumerable<WorkshopItem> source = Items;
        if (q.Length > 0)
            source = source.Where(i => i.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
        foreach (var item in source) FilteredItems.Add(item);
    }

    private async Task LoadItemPreviewAsync(WorkshopItem item)
    {
        if (string.IsNullOrEmpty(item.PreviewImageUrl)) return;

        try
        {
            Log.Debug($"Loading thumbnail for '{item.Title}': {item.PreviewImageUrl}");
            var response = await HttpClient.GetAsync(item.PreviewImageUrl);
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync();
                item.PreviewBitmap = new Bitmap(stream);
                Log.Debug($"Thumbnail loaded for '{item.Title}'");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to load thumbnail for '{item.Title}': {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchQuery = string.Empty;

    /// <summary>
    /// Click handler for the mod card. In normal mode it bubbles up through
    /// <see cref="ItemSelected"/> so the shell opens the editor; in selection
    /// mode it just toggles <see cref="WorkshopItem.IsSelected"/>.
    /// </summary>
    [RelayCommand]
    private void SelectItem(WorkshopItem item)
    {
        if (SelectionMode)
        {
            item.IsSelected = !item.IsSelected;
            return;
        }

        ItemSelected?.Invoke(item);
    }

    [RelayCommand]
    private void CreateNewItem()
    {
        CreateRequested?.Invoke();
    }

    // ─── Selection-mode lifecycle ─────────────────────────────────────────────

    [RelayCommand]
    private void EnterSelectionMode()
    {
        if (SelectionMode) return;
        // Rewire IsSelected change tracking only while we care about the count.
        foreach (var item in Items)
            item.PropertyChanged += OnItemPropertyChanged;
        SelectionMode = true;
    }

    [RelayCommand]
    private void ExitSelectionMode()
    {
        if (!SelectionMode) return;
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            item.IsSelected = false;
        }
        SelectionMode = false;
        SelectedCount = 0;
        ShowBulkDeleteConfirmation = false;
        BulkDeleteTypedConfirmation = string.Empty;
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items) item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Items) item.IsSelected = false;
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkshopItem.IsSelected))
            RecomputeSelectionState();
    }

    private void RecomputeSelectionState()
    {
        SelectedCount = Items.Count(i => i.IsSelected);
        OnPropertyChanged(nameof(AllSelected));
    }

    // ─── Bulk visibility ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SetSelectedVisibilityAsync(VisibilityType visibility)
    {
        if (IsBulkProcessing || !HasSelection) return;

        var targets = Items.Where(i => i.IsSelected).ToList();
        IsBulkProcessing = true;
        var failed = new List<string>();
        try
        {
            foreach (var item in targets)
            {
                try
                {
                    var ok = await _steamService.UpdateItemAsync(
                        item.PublishedFileId,
                        title: null, description: null,
                        contentFolderPath: null, previewImagePath: null,
                        visibility: visibility,
                        tags: null, changelog: null);

                    if (ok)
                        TelemetryService.Instance?.Track(TelemetryEventTypes.ModUpdated, AppConfig.AppId);
                    else
                        failed.Add(item.Title);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Bulk visibility update failed for '{item.Title}': {ex.Message}");
                    failed.Add(item.Title);
                }
            }
        }
        finally
        {
            IsBulkProcessing = false;
        }

        if (failed.Count == 0)
        {
            _notifications?.ShowSuccess(string.Format(Loc["BulkVisibilityUpdated"], targets.Count));
        }
        else
        {
            _notifications?.ShowError(string.Format(
                Loc["BulkVisibilityPartialFail"],
                targets.Count - failed.Count, targets.Count));
        }

        ExitSelectionMode();
        await LoadItemsAsync();
    }

    // ─── Bulk delete ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void RequestBulkDelete()
    {
        if (!HasSelection || IsBulkProcessing) return;
        BulkDeleteTypedConfirmation = string.Empty;
        ShowBulkDeleteConfirmation = true;
    }

    [RelayCommand]
    private void CancelBulkDelete()
    {
        ShowBulkDeleteConfirmation = false;
        BulkDeleteTypedConfirmation = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmBulkDeleteAsync()
    {
        if (!IsBulkDeleteConfirmed || IsBulkProcessing) return;

        var targets = Items.Where(i => i.IsSelected).ToList();
        if (targets.Count == 0)
        {
            ShowBulkDeleteConfirmation = false;
            return;
        }

        IsBulkProcessing = true;
        var failed = new List<string>();
        try
        {
            foreach (var item in targets)
            {
                try
                {
                    var ok = await _steamService.DeleteItemAsync(item.PublishedFileId);
                    if (ok)
                    {
                        TelemetryService.Instance?.Track(TelemetryEventTypes.ModDeleted, AppConfig.AppId);
                    }
                    else
                    {
                        failed.Add(item.Title);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Bulk delete failed for '{item.Title}': {ex.Message}");
                    failed.Add(item.Title);
                }
            }
        }
        finally
        {
            IsBulkProcessing = false;
        }

        var deleted = targets.Count - failed.Count;
        if (failed.Count == 0)
            _notifications?.ShowSuccess(string.Format(Loc["BulkDeleteSuccess"], deleted));
        else
            _notifications?.ShowError(string.Format(Loc["BulkDeletePartialFail"], deleted, targets.Count));

        ShowBulkDeleteConfirmation = false;
        BulkDeleteTypedConfirmation = string.Empty;
        ExitSelectionMode();
        await LoadItemsAsync();
    }
}