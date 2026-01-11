using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services;

namespace SteamWorkshopManager.ViewModels;

public partial class CreateItemViewModel : ViewModelBase
{
    private readonly ISteamService _steamService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IProgress<UploadProgress>? _uploadProgress;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _previewImagePath;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private string? _contentFolderPath;

    [ObservableProperty]
    private VisibilityType _visibility = VisibilityType.Private;

    [ObservableProperty]
    private string _initialChangelog = string.Empty;

    [ObservableProperty]
    private bool _isCreating;

    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<TagCategory> TagCategories { get; } = [];

    public static IEnumerable<VisibilityType> VisibilityOptions =>
        Enum.GetValues<VisibilityType>();

    public event Action? CloseRequested;
    public event Action? ItemCreated;

    public CreateItemViewModel(ISteamService steamService, IFileDialogService fileDialogService,
        IProgress<UploadProgress>? uploadProgress = null)
    {
        _steamService = steamService;
        _fileDialogService = fileDialogService;
        _uploadProgress = uploadProgress;

        // Charger les tags par catégorie
        foreach (var (category, tags) in WorkshopTags.TagsByCategory)
        {
            var tagCategory = new TagCategory { Name = category };
            foreach (var tag in tags)
            {
                tagCategory.Tags.Add(new WorkshopTag(tag, false));
            }
            TagCategories.Add(tagCategory);
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

            // Auto-remplir le titre si vide
            if (string.IsNullOrEmpty(Title))
            {
                Title = Path.GetFileName(path) ?? "Nouveau mod";
            }
        }
    }

    public void HandleFolderDrop(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            ContentFolderPath = folderPath;

            if (string.IsNullOrEmpty(Title))
            {
                Title = Path.GetFileName(folderPath) ?? "Nouveau mod";
            }
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Le titre est obligatoire";
            return;
        }

        if (string.IsNullOrWhiteSpace(ContentFolderPath))
        {
            ErrorMessage = "Le dossier du mod est obligatoire";
            return;
        }

        if (!Directory.Exists(ContentFolderPath))
        {
            ErrorMessage = "Le dossier spécifié n'existe pas";
            return;
        }

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var selectedTags = TagCategories
                .SelectMany(c => c.Tags)
                .Where(t => t.IsSelected)
                .Select(t => t.Name)
                .ToList();

            var fileId = await _steamService.CreateItemAsync(
                Title,
                Description,
                ContentFolderPath,
                PreviewImagePath,
                Visibility,
                selectedTags,
                string.IsNullOrWhiteSpace(InitialChangelog) ? "Version initiale" : InitialChangelog,
                _uploadProgress
            );

            if (fileId.HasValue)
            {
                ItemCreated?.Invoke();
            }
            else
            {
                ErrorMessage = "Échec de la création de l'item";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsCreating = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke();
    }
}
