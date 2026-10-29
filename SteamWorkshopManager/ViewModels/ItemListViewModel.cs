using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Platforms;
using SteamWorkshopManager.Services.Telemetry;
using SteamWorkshopManager.Services.Workshop;

namespace SteamWorkshopManager.ViewModels;

public partial class ItemListViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<ItemListViewModel>();
    private readonly ModPlatforms _platforms;
    private readonly INotificationService? _notifications;
    private readonly ITelemetryService _telemetry;
    private readonly ISessionContext _context;
    private readonly ThumbnailCache _thumbnails;
    private readonly PublishedItemsCache _itemsCache;

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

    /// <summary>Inline sign-in, shown on a mod.io session while signed out (or after the token expired).</summary>
    public ModioSignInViewModel ModioSignIn { get; }

    public bool ShowModioSignIn => _context.Platform == WorkshopPlatform.ModIo && !ModioSignIn.IsSignedIn;

    /// <summary>Friends-only and unlisted exist on Steam only; mod.io mods are public or hidden.</summary>
    public bool HasSteamVisibilities => _context.Platform != WorkshopPlatform.ModIo;

    private string PlatformName => Loc[_context.Platform == WorkshopPlatform.ModIo ? "PlatformModio" : "PlatformSteam"];

    public string SubtitleText => string.Format(Loc["MyPublicationsSubtitle"], PlatformName);

    public string RefreshingText => string.Format(Loc["ItemRefreshing"], PlatformName);

    public string BulkDeleteWarningText => string.Format(Loc["BulkDeleteWarning"], PlatformName);

    /// <summary>Platform-dependent texts and options, refreshed when the session changes.</summary>
    public void OnSessionChanged()
    {
        OnPropertyChanged(nameof(ShowModioSignIn));
        OnPropertyChanged(nameof(HasSteamVisibilities));
        OnPlatformTextsChanged();
    }

    private void OnPlatformTextsChanged()
    {
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(RefreshingText));
        OnPropertyChanged(nameof(BulkDeleteWarningText));
    }

    public event Action<WorkshopItem>? ItemSelected;
    public event Action? CreateRequested;

    public ItemListViewModel(
        ModPlatforms platforms,
        ITelemetryService telemetry,
        ISessionContext context,
        ThumbnailCache thumbnails,
        PublishedItemsCache itemsCache,
        ModioSignInViewModel modioSignIn,
        INotificationService? notifications = null)
    {
        ModioSignIn = modioSignIn;
        ModioSignIn.SignedInChanged += () => OnPropertyChanged(nameof(ShowModioSignIn));
        LocalizationService.Instance.LanguageChanged += OnPlatformTextsChanged;
        _platforms = platforms;
        _telemetry = telemetry;
        _context = context;
        _thumbnails = thumbnails;
        _itemsCache = itemsCache;
        _notifications = notifications;
        Items.CollectionChanged += OnItemsChanged;
    }

    /// <summary>Raised after a platform refresh changed or removed items that were on screen.</summary>
    public event Action<ItemsReconciliation>? ItemsReconciled;

    // A newer load (session switch, manual refresh) supersedes an older one still in flight.
    private int _loadGeneration;

    /// <summary>
    /// Shows the cached list right away when nothing is on screen, then asks the platform and
    /// merges its answer: cards being checked spin, unchanged ones keep their instance.
    /// </summary>
    [RelayCommand]
    public async Task LoadItemsAsync()
    {
        OnPropertyChanged(nameof(ShowModioSignIn));
        OnPropertyChanged(nameof(HasSteamVisibilities));
        OnPlatformTextsChanged();
        if (_context.Current is not { } session || _platforms.Current is not { } platform) return;
        if (!platform.IsReady)
        {
            ErrorMessage = Loc[platform.NotReadyMessageKey];
            return;
        }

        var generation = ++_loadGeneration;
        var accountId = platform.AccountId;
        ErrorMessage = null;

        if (Items.Count == 0)
        {
            IsLoading = true;
            var cached = await _itemsCache.LoadAsync(session, accountId);
            if (generation != _loadGeneration) return;
            if (cached is { Count: > 0 }) ShowItems(cached);
        }

        // The full-page loader only shows when there is nothing to show yet.
        IsLoading = Items.Count == 0;
        foreach (var item in Items) item.IsRefreshing = true;

        try
        {
            var fresh = await platform.GetMyModsAsync();
            if (generation != _loadGeneration) return;

            var outcome = PublishedItemsReconciler.Reconcile(Items, fresh);
            ShowItems(outcome.Next, released: outcome.Removed.Concat(outcome.Replaced.Select(r => r.Old)));
            _ = _itemsCache.SaveAsync(session, accountId, Items);

            if (outcome.Replaced.Count > 0 || outcome.Removed.Count > 0)
                ItemsReconciled?.Invoke(outcome);
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration) return;
            // Cached cards stay: the platform just could not confirm them.
            if (Items.Count == 0) ErrorMessage = $"{Loc["LoadingError"]}: {ex.Message}";
            else _notifications?.ShowError($"{Loc["ItemsRefreshFailed"]}: {ex.Message}");
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                foreach (var item in Items) item.IsRefreshing = false;
                IsLoading = false;
                HasNoItems = Items.Count == 0;
            }
        }
    }

    /// <summary>Puts <paramref name="items"/> on screen, dropping the thumbnails of <paramref name="released"/>.</summary>
    private void ShowItems(IReadOnlyList<WorkshopItem> items, IEnumerable<WorkshopItem>? released = null)
    {
        foreach (var gone in released ?? [])
        {
            gone.PreviewBitmap = null;
            gone.IsRefreshing = false;
            gone.PropertyChanged -= OnItemPropertyChanged;
            _thumbnailRequested.Remove(gone);
        }

        // One filter pass for the whole batch instead of one per Add.
        _suspendItemsChanged = true;
        try
        {
            var kept = new HashSet<WorkshopItem>(Items, ReferenceEqualityComparer.Instance);
            Items.Clear();
            foreach (var item in items)
            {
                if (SelectionMode && !kept.Contains(item)) item.PropertyChanged += OnItemPropertyChanged;
                Items.Add(item);
            }
        }
        finally
        {
            _suspendItemsChanged = false;
        }
        ApplyFilter();
        RecomputeSelectionState();
        HasNoItems = Items.Count == 0;

        // The grid asks for its visible thumbnails itself; the home dashboard shows the recent ones.
        foreach (var item in items.OrderByDescending(i => i.UpdatedAt).Take(RecentItemsCount))
            RequestThumbnail(item);
    }

    /// <summary>
    /// Single-item refresh for post-Create / post-Update flows. Avoids the
    /// full re-query <see cref="LoadItemsAsync"/> performs. Returns
    /// the live <see cref="WorkshopItem"/> instance now in <see cref="Items"/>,
    /// or null if the platform couldn't resolve the id (caller decides whether to
    /// fall back to a full reload).
    /// </summary>
    public async Task<WorkshopItem?> RefreshItemAsync(ulong id)
    {
        if (_context.Current is not { } session || _platforms.Current is not { IsReady: true } platform) return null;

        WorkshopItem? fetched;
        try
        {
            fetched = await platform.GetModAsync(id);
        }
        catch (Exception ex)
        {
            Log.Warning($"RefreshItemAsync failed for {id}: {ex.Message}");
            return null;
        }
        if (fetched is null) return null;

        var existingIndex = -1;
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id == id) { existingIndex = i; break; }
        }

        if (existingIndex >= 0)
        {
            // Replace in place so list position + scroll anchor are preserved.
            var stale = Items[existingIndex];
            stale.PreviewBitmap = null;
            stale.IsRefreshing = false;
            stale.PropertyChanged -= OnItemPropertyChanged;
            Items[existingIndex] = fetched;
        }
        else
        {
            // Newly created item: prepend (lists are ordered by creation desc).
            Items.Insert(0, fetched);
        }

        HasNoItems = Items.Count == 0;
        RequestThumbnail(fetched);
        _ = _itemsCache.SaveAsync(session, platform.AccountId, Items);
        return fetched;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private bool _suspendItemsChanged;

    // Cancelled when the list is cleared so late thumbnails don't land on detached items.
    private CancellationTokenSource _thumbnailCts = new();

    // Thumbnails load on demand (card shown, or recent on the home page), once per item.
    private readonly HashSet<WorkshopItem> _thumbnailRequested = new(ReferenceEqualityComparer.Instance);

    /// <summary>Number of recent items the home dashboard shows.</summary>
    public const int RecentItemsCount = 8;

    public void RequestThumbnail(WorkshopItem item)
    {
        if (item.PreviewBitmap is not null || !_thumbnailRequested.Add(item)) return;
        _ = LoadItemPreviewAsync(item);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suspendItemsChanged) return;
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
        _loadGeneration++;
        _thumbnailCts.Cancel();
        _thumbnailCts.Dispose();
        _thumbnailCts = new CancellationTokenSource();
        _thumbnailRequested.Clear();

        foreach (var item in Items)
        {
            item.PreviewBitmap = null;
            item.IsRefreshing = false;
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

        var ct = _thumbnailCts.Token;
        var bitmap = await _thumbnails.GetAsync(item.PreviewImageUrl, ct);
        if (bitmap is null) return;

        if (ct.IsCancellationRequested || !Items.Contains(item))
        {
            bitmap.Dispose();
            return;
        }
        item.PreviewBitmap = bitmap;
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

    // No telemetry for mod.io for now.
    private void TrackSteam(string eventType)
    {
        if (_context.Platform == WorkshopPlatform.Steam) _telemetry.Track(eventType, _context.AppId);
    }

    // ─── Bulk visibility ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SetSelectedVisibilityAsync(VisibilityType visibility)
    {
        if (IsBulkProcessing || !HasSelection) return;
        if (_platforms.Current is not { } platform) return;

        var targets = Items.Where(i => i.IsSelected).ToList();
        IsBulkProcessing = true;
        var failed = new List<string>();
        try
        {
            foreach (var item in targets)
            {
                try
                {
                    var ok = await platform.SetVisibilityAsync(item.Id, visibility);

                    if (ok)
                        TrackSteam(TelemetryEventTypes.ModUpdated);
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
        if (_platforms.Current is not { } platform) return;

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
                    var ok = await platform.DeleteModAsync(item.Id);
                    if (ok)
                    {
                        TrackSteam(TelemetryEventTypes.ModDeleted);
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