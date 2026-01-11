using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IProgress<UploadProgress>? _uploadProgress;
    private readonly string? _initialContentFolderPath;
    private readonly long _initialFolderSize;
    private readonly DateTime _initialFolderModified;
    private readonly string? _initialPreviewImagePath;
    private readonly long _initialImageSize;
    private readonly DateTime _initialImageModified;
    private static readonly HttpClient HttpClient = new();
    private const long MaxImageSizeBytes = 1024 * 1024; // 1 MB Steam limit

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImageSize))]
    [NotifyPropertyChangedFor(nameof(IsImageTooLarge))]
    private string? _previewImagePath;

    [ObservableProperty]
    private Bitmap? _previewImage;

    public string PreviewImageSize => FormatImageSize(PreviewImagePath);
    public bool IsImageTooLarge => GetImageSize(PreviewImagePath) > MaxImageSizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentFolderSize))]
    private string? _contentFolderPath;

    public string ContentFolderSize => FormatFolderSize(ContentFolderPath);

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

    [ObservableProperty]
    private string _newCustomTag = string.Empty;

    public ObservableCollection<TagCategory> TagCategories { get; } = [];
    public ObservableCollection<WorkshopTag> CustomTags { get; } = [];
    public ObservableCollection<ItemVersion> Versions { get; } = [];

    public static IEnumerable<VisibilityType> VisibilityOptions =>
        Enum.GetValues<VisibilityType>();

    public event Action? CloseRequested;
    public event Action? ItemUpdated;
    public event Action? ItemDeleted;

    public ItemEditorViewModel(WorkshopItem item, ISteamService steamService, IFileDialogService fileDialogService,
        ISettingsService settingsService, INotificationService notificationService, IProgress<UploadProgress>? uploadProgress = null)
    {
        _originalItem = item;
        _steamService = steamService;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _uploadProgress = uploadProgress;

        _title = item.Title;
        _description = item.Description;
        _visibility = item.Visibility;

        // Load saved preview image path from settings (fallback to item's path)
        _previewImagePath = settingsService.GetPreviewImagePath((ulong)item.PublishedFileId) ?? item.PreviewImagePath;
        _initialPreviewImagePath = _previewImagePath;
        (_initialImageSize, _initialImageModified) = GetFileInfo(_previewImagePath);

        // Load saved content folder path from settings
        _contentFolderPath = settingsService.GetContentFolderPath((ulong)item.PublishedFileId);
        _initialContentFolderPath = _contentFolderPath;
        (_initialFolderSize, _initialFolderModified) = GetFolderInfo(_contentFolderPath);

        // Load tags by category
        var selectedTagNames = item.Tags.Select(t => t.Name).ToHashSet();
        var allPredefinedTags = WorkshopTags.TagsByCategory
            .SelectMany(kvp => kvp.Value.Concat(kvp.Value.Select(v => $"{kvp.Key}: {v}")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (category, tags) in WorkshopTags.TagsByCategory)
        {
            var tagCategory = new TagCategory { Name = category };
            foreach (var tag in tags)
            {
                // Check if tag is selected (with or without category prefix)
                var isSelected = selectedTagNames.Contains(tag) || selectedTagNames.Contains($"{category}: {tag}");
                tagCategory.Tags.Add(new WorkshopTag(tag, isSelected));
            }
            TagCategories.Add(tagCategory);
        }

        // Load custom tags from settings
        foreach (var customTag in settingsService.GetCustomTags())
        {
            var isSelected = selectedTagNames.Contains(customTag);
            CustomTags.Add(new WorkshopTag(customTag, isSelected));
        }

        // Add item tags that are not predefined and not in custom tags
        foreach (var tagName in selectedTagNames)
        {
            if (!allPredefinedTags.Contains(tagName) &&
                !CustomTags.Any(ct => ct.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
            {
                // This is a custom tag from the item, add it to settings and list
                settingsService.AddCustomTag(tagName);
                CustomTags.Add(new WorkshopTag(tagName, true));
            }
        }

        // Load versions
        foreach (var version in item.Versions)
        {
            Versions.Add(version);
        }

        // Load preview image
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
            Loc["SelectPreviewImage"],
            ".png", ".jpg", ".jpeg", ".gif"
        );

        if (!string.IsNullOrEmpty(path))
        {
            PreviewImagePath = path;
            // Save the path for this mod
            _settingsService.SetPreviewImagePath((ulong)_originalItem.PublishedFileId, path);
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
            Loc["ContentFolder"]
        );

        if (!string.IsNullOrEmpty(path))
        {
            ContentFolderPath = path;
            // Save the path for this mod
            _settingsService.SetContentFolderPath((ulong)_originalItem.PublishedFileId, path);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = Loc["TitleRequired"];
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
                .Concat(CustomTags.Where(t => t.IsSelected).Select(t => t.Name))
                .ToList();

            // Only send content folder if it changed (path, size, or modification date)
            var contentFolder = HasContentFolderChanged() ? ContentFolderPath : null;

            // Only send preview image if it changed (path, size, or modification date)
            var previewImage = HasPreviewImageChanged() ? PreviewImagePath : null;

            var success = await _steamService.UpdateItemAsync(
                _originalItem.PublishedFileId,
                Title != _originalItem.Title ? Title : null,
                Description != _originalItem.Description ? Description : null,
                contentFolder,
                previewImage,
                Visibility != _originalItem.Visibility ? Visibility : null,
                selectedTags,
                string.IsNullOrWhiteSpace(NewChangelog) ? null : NewChangelog,
                _uploadProgress
            );

            if (success)
            {
                NewChangelog = string.Empty;
                _notificationService.ShowSuccess(Loc["ItemUpdatedSuccess"]);
                ItemUpdated?.Invoke();
            }
            else
            {
                _notificationService.ShowError(Loc["UpdateFailed"]);
                ErrorMessage = Loc["UpdateFailed"];
            }
        }
        catch (Exception ex)
        {
            _notificationService.ShowError(Loc["UpdateFailed"]);
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
                _notificationService.ShowSuccess(Loc["ItemDeletedSuccess"]);
                ItemDeleted?.Invoke();
            }
            else
            {
                _notificationService.ShowError(Loc["DeleteFailed"]);
                ErrorMessage = Loc["DeleteFailed"];
                ShowDeleteConfirmation = false;
            }
        }
        catch (Exception ex)
        {
            _notificationService.ShowError(Loc["DeleteFailed"]);
            ErrorMessage = $"{Loc["DeleteFailed"]}: {ex.Message}";
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

    [RelayCommand]
    private void AddCustomTag()
    {
        if (string.IsNullOrWhiteSpace(NewCustomTag))
            return;

        var tagName = NewCustomTag.Trim();

        // Check if tag already exists
        if (CustomTags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
        {
            NewCustomTag = string.Empty;
            return;
        }

        // Add to settings and list
        _settingsService.AddCustomTag(tagName);
        CustomTags.Add(new WorkshopTag(tagName, true));
        NewCustomTag = string.Empty;
    }

    [RelayCommand]
    private void RemoveCustomTag(WorkshopTag tag)
    {
        if (tag == null) return;

        _settingsService.RemoveCustomTag(tag.Name);
        CustomTags.Remove(tag);
    }

    /// <summary>
    /// Formats the folder size for display.
    /// </summary>
    private static string FormatFolderSize(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return string.Empty;

        var (size, _) = GetFolderInfo(folderPath);
        return size switch
        {
            < 1024 => $"{size} B",
            < 1024 * 1024 => $"{size / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{size / (1024.0 * 1024):F1} MB",
            _ => $"{size / (1024.0 * 1024 * 1024):F2} GB"
        };
    }

    /// <summary>
    /// Gets the total size and last modified date of a folder.
    /// </summary>
    private static (long size, DateTime modified) GetFolderInfo(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return (0, DateTime.MinValue);

        try
        {
            var dirInfo = new DirectoryInfo(folderPath);
            var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
            var totalSize = files.Sum(f => f.Length);
            var lastModified = files.Length > 0
                ? files.Max(f => f.LastWriteTimeUtc)
                : dirInfo.LastWriteTimeUtc;
            return (totalSize, lastModified);
        }
        catch
        {
            return (0, DateTime.MinValue);
        }
    }

    /// <summary>
    /// Checks if the content folder has changed (different path, size, or modification date).
    /// </summary>
    private bool HasContentFolderChanged()
    {
        // Path changed
        if (ContentFolderPath != _initialContentFolderPath)
            return true;

        // No folder set
        if (string.IsNullOrEmpty(ContentFolderPath))
            return false;

        // Check size and modification date
        var (currentSize, currentModified) = GetFolderInfo(ContentFolderPath);
        return currentSize != _initialFolderSize || currentModified != _initialFolderModified;
    }

    /// <summary>
    /// Checks if the preview image has changed (different path, size, or modification date).
    /// </summary>
    private bool HasPreviewImageChanged()
    {
        // Path changed
        if (PreviewImagePath != _initialPreviewImagePath)
            return true;

        // No image set
        if (string.IsNullOrEmpty(PreviewImagePath))
            return false;

        // Check size and modification date
        var (currentSize, currentModified) = GetFileInfo(PreviewImagePath);
        return currentSize != _initialImageSize || currentModified != _initialImageModified;
    }

    /// <summary>
    /// Gets file size and last modified date.
    /// </summary>
    private static (long size, DateTime modified) GetFileInfo(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return (0, DateTime.MinValue);

        try
        {
            var fileInfo = new FileInfo(filePath);
            return (fileInfo.Length, fileInfo.LastWriteTimeUtc);
        }
        catch
        {
            return (0, DateTime.MinValue);
        }
    }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    private static long GetImageSize(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return 0;

        try
        {
            return new FileInfo(filePath).Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Formats the image file size for display.
    /// </summary>
    private static string FormatImageSize(string? filePath)
    {
        var size = GetImageSize(filePath);
        if (size == 0)
            return string.Empty;

        return size switch
        {
            < 1024 => $"{size} B",
            < 1024 * 1024 => $"{size / 1024.0:F1} KB",
            _ => $"{size / (1024.0 * 1024):F2} MB"
        };
    }
}
