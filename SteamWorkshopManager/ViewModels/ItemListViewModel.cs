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
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.ViewModels;

public partial class ItemListViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<ItemListViewModel>();
    private readonly ISteamService _steamService;
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

    public ObservableCollection<WorkshopItem> Items { get; } = [];

    public ObservableCollection<WorkshopItem> FilteredItems { get; } = [];

    public event Action<WorkshopItem>? ItemSelected;
    public event Action? CreateRequested;

    public ItemListViewModel(ISteamService steamService)
    {
        _steamService = steamService;
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

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyFilter();

    /// <summary>
    /// Releases each item's cached thumbnail before clearing the collection.
    /// ObservableCollection.Clear() only drops references; without explicit
    /// disposal the native Skia surfaces accumulate across session switches.
    /// </summary>
    public void DisposeAndClearItems()
    {
        foreach (var item in Items)
            item.PreviewBitmap = null;
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

    [RelayCommand]
    private void SelectItem(WorkshopItem item)
    {
        ItemSelected?.Invoke(item);
    }

    [RelayCommand]
    private void CreateNewItem()
    {
        CreateRequested?.Invoke();
    }
}