using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services;

namespace SteamWorkshopManager.ViewModels;

public partial class ItemListViewModel : ViewModelBase
{
    private readonly ISteamService _steamService;
    private static readonly HttpClient HttpClient = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

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
            ErrorMessage = "Steam n'est pas initialisé";
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
                // Charger l'image en arrière-plan
                _ = LoadItemPreviewAsync(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadItemPreviewAsync(WorkshopItem item)
    {
        if (string.IsNullOrEmpty(item.PreviewImageUrl)) return;

        try
        {
            Console.WriteLine($"[DEBUG] Loading thumbnail for '{item.Title}': {item.PreviewImageUrl}");
            var response = await HttpClient.GetAsync(item.PreviewImageUrl);
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync();
                item.PreviewBitmap = new Bitmap(stream);
                Console.WriteLine($"[DEBUG] Thumbnail loaded for '{item.Title}'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Failed to load thumbnail for '{item.Title}': {ex.Message}");
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
