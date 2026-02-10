using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;

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

    public ObservableCollection<WorkshopItem> Items { get; } = [];

    public event Action<WorkshopItem>? ItemSelected;
    public event Action? CreateRequested;

    public ItemListViewModel(ISteamService steamService)
    {
        _steamService = steamService;
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
            Items.Clear();
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
