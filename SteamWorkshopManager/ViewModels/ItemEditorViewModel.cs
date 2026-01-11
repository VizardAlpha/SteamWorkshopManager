using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services;

namespace SteamWorkshopManager.ViewModels;

public partial class ItemEditorViewModel : ViewModelBase
{
    private readonly WorkshopItem _originalItem;
    private readonly ISteamService _steamService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IProgress<UploadProgress>? _uploadProgress;
    private static readonly HttpClient HttpClient = new();

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string? _previewImagePath;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private string? _contentFolderPath;

    [ObservableProperty]
    private VisibilityType _visibility;

    [ObservableProperty]
    private string _newChangelog = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _selectedTabIndex;

    public ObservableCollection<TagCategory> TagCategories { get; } = [];
    public ObservableCollection<ItemVersion> Versions { get; } = [];

    public static IEnumerable<VisibilityType> VisibilityOptions =>
        Enum.GetValues<VisibilityType>();

    public event Action? CloseRequested;
    public event Action? ItemDeleted;

    public ItemEditorViewModel(WorkshopItem item, ISteamService steamService, IFileDialogService fileDialogService,
        IProgress<UploadProgress>? uploadProgress = null)
    {
        _originalItem = item;
        _steamService = steamService;
        _fileDialogService = fileDialogService;
        _uploadProgress = uploadProgress;

        _title = item.Title;
        _description = item.Description;
        _previewImagePath = item.PreviewImagePath;
        _contentFolderPath = item.ContentFolderPath;
        _visibility = item.Visibility;

        // Charger les tags par catégorie
        var selectedTagNames = item.Tags.Select(t => t.Name).ToHashSet();
        foreach (var (category, tags) in WorkshopTags.TagsByCategory)
        {
            var tagCategory = new TagCategory { Name = category };
            foreach (var tag in tags)
            {
                // Vérifier si le tag est sélectionné (avec ou sans préfixe de catégorie)
                var isSelected = selectedTagNames.Contains(tag) || selectedTagNames.Contains($"{category}: {tag}");
                tagCategory.Tags.Add(new WorkshopTag(tag, isSelected));
            }
            TagCategories.Add(tagCategory);
        }

        // Charger les versions
        foreach (var version in item.Versions)
        {
            Versions.Add(version);
        }

        // Charger l'image de preview
        LoadPreviewImageAsync(item.PreviewImageUrl);
    }

    private async void LoadPreviewImageAsync(string? url)
    {
        Console.WriteLine($"[DEBUG] LoadPreviewImageAsync called with URL: {url ?? "null"}");

        if (string.IsNullOrEmpty(url))
        {
            Console.WriteLine("[DEBUG] URL is null or empty, skipping image load");
            return;
        }

        try
        {
            Console.WriteLine($"[DEBUG] Downloading image from: {url}");
            var response = await HttpClient.GetAsync(url);
            Console.WriteLine($"[DEBUG] Response status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync();
                PreviewImage = new Bitmap(stream);
                Console.WriteLine("[DEBUG] Image loaded successfully");
            }
            else
            {
                Console.WriteLine($"[DEBUG] Failed to download image: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Exception loading image: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task BrowsePreviewImageAsync()
    {
        var path = await _fileDialogService.OpenFileAsync(
            "Sélectionner une image de prévisualisation",
            ".png", ".jpg", ".jpeg", ".gif"
        );

        if (!string.IsNullOrEmpty(path))
        {
            PreviewImagePath = path;
            try
            {
                PreviewImage = new Bitmap(path);
            }
            catch
            {
                // Ignore
            }
        }
    }

    [RelayCommand]
    private async Task BrowseContentFolderAsync()
    {
        var path = await _fileDialogService.OpenFolderAsync(
            "Sélectionner le dossier du mod"
        );

        if (!string.IsNullOrEmpty(path))
        {
            ContentFolderPath = path;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Le titre est obligatoire";
            return;
        }

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            var selectedTags = TagCategories
                .SelectMany(c => c.Tags)
                .Where(t => t.IsSelected)
                .Select(t => t.Name)
                .ToList();

            var success = await _steamService.UpdateItemAsync(
                _originalItem.PublishedFileId,
                Title != _originalItem.Title ? Title : null,
                Description != _originalItem.Description ? Description : null,
                ContentFolderPath,
                PreviewImagePath != _originalItem.PreviewImagePath ? PreviewImagePath : null,
                Visibility != _originalItem.Visibility ? Visibility : null,
                selectedTags,
                string.IsNullOrWhiteSpace(NewChangelog) ? null : NewChangelog,
                _uploadProgress
            );

            if (success)
            {
                NewChangelog = string.Empty;
                CloseRequested?.Invoke();
            }
            else
            {
                ErrorMessage = "Échec de la mise à jour";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void ShowDeleteDialog()
    {
        ShowDeleteConfirmation = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirmation = false;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        IsDeleting = true;
        ErrorMessage = null;

        try
        {
            var success = await _steamService.DeleteItemAsync(_originalItem.PublishedFileId);

            if (success)
            {
                ItemDeleted?.Invoke();
            }
            else
            {
                ErrorMessage = "Échec de la suppression";
                ShowDeleteConfirmation = false;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur : {ex.Message}";
            ShowDeleteConfirmation = false;
        }
        finally
        {
            IsDeleting = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }
}
