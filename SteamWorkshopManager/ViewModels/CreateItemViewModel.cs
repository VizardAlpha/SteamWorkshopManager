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
using SteamWorkshopManager.Services.Interfaces;

namespace SteamWorkshopManager.ViewModels;

public partial class CreateItemViewModel : ViewModelBase
{
    private readonly ISteamService _steamService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IProgress<UploadProgress>? _uploadProgress;
    private const long MaxImageSizeBytes = 1024 * 1024; // 1 MB Steam limit

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImageSize))]
    [NotifyPropertyChangedFor(nameof(IsImageTooLarge))]
    private string? _previewImagePath;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentFolderSize))]
    private string? _contentFolderPath;

    public string ContentFolderSize => FormatFolderSize(ContentFolderPath);
    public string PreviewImageSize => FormatFileSize(PreviewImagePath);
    public bool IsImageTooLarge => GetFileSize(PreviewImagePath) > MaxImageSizeBytes;

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

    [ObservableProperty]
    private string _newCustomTag = string.Empty;

    public ObservableCollection<TagCategory> TagCategories { get; } = [];
    public ObservableCollection<WorkshopTag> CustomTags { get; } = [];

    public static IEnumerable<VisibilityType> VisibilityOptions =>
        Enum.GetValues<VisibilityType>();

    public event Action? CloseRequested;
    public event Action? ItemCreated;

    public CreateItemViewModel(ISteamService steamService, IFileDialogService fileDialogService,
        ISettingsService settingsService, INotificationService notificationService, IProgress<UploadProgress>? uploadProgress = null)
    {
        _steamService = steamService;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _uploadProgress = uploadProgress;

        // Load tags by category from current session
        var sessionTags = AppConfig.CurrentSession?.TagsByCategory ?? new Dictionary<string, List<string>>();
        foreach (var (category, tags) in sessionTags)
        {
            var tagCategory = new TagCategory { Name = category };
            foreach (var tag in tags)
            {
                tagCategory.Tags.Add(new WorkshopTag(tag, false));
            }
            TagCategories.Add(tagCategory);
        }

        // Load custom tags from current session
        var sessionCustomTags = AppConfig.CurrentSession?.CustomTags ?? [];
        foreach (var customTag in sessionCustomTags)
        {
            CustomTags.Add(new WorkshopTag(customTag, false));
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

            // Auto-fill title if empty
            if (string.IsNullOrEmpty(Title))
            {
                Title = Path.GetFileName(path) ?? "New mod";
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
                Title = Path.GetFileName(folderPath) ?? "New mod";
            }
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = Loc["TitleRequired"];
            return;
        }

        if (string.IsNullOrWhiteSpace(ContentFolderPath))
        {
            ErrorMessage = Loc["FolderRequired"];
            return;
        }

        if (!Directory.Exists(ContentFolderPath))
        {
            ErrorMessage = Loc["FolderNotExist"];
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
                .Concat(CustomTags.Where(t => t.IsSelected).Select(t => t.Name))
                .ToList();

            var fileId = await _steamService.CreateItemAsync(
                Title,
                Description,
                ContentFolderPath,
                PreviewImagePath,
                Visibility,
                selectedTags,
                string.IsNullOrWhiteSpace(InitialChangelog) ? "Initial version" : InitialChangelog,
                _uploadProgress
            );

            if (fileId.HasValue)
            {
                // Save paths for this new mod
                _settingsService.SetContentFolderPath((ulong)fileId.Value, ContentFolderPath);
                if (!string.IsNullOrEmpty(PreviewImagePath))
                {
                    _settingsService.SetPreviewImagePath((ulong)fileId.Value, PreviewImagePath);
                }
                _notificationService.ShowSuccess(Loc["ItemCreatedSuccess"]);
                ItemCreated?.Invoke();
            }
            else
            {
                _notificationService.ShowError(Loc["CreationFailed"]);
                ErrorMessage = Loc["CreationFailed"];
            }
        }
        catch (Exception ex)
        {
            _notificationService.ShowError(Loc["CreationFailed"]);
            ErrorMessage = $"{Loc["CreationFailed"]}: {ex.Message}";
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

        // Add to session and list
        AddCustomTagToSession(tagName);
        CustomTags.Add(new WorkshopTag(tagName, true));
        NewCustomTag = string.Empty;
    }

    [RelayCommand]
    private void RemoveCustomTag(WorkshopTag tag)
    {
        if (tag == null) return;

        RemoveCustomTagFromSession(tag.Name);
        CustomTags.Remove(tag);
    }

    /// <summary>
    /// Adds a custom tag to the current session and saves it.
    /// </summary>
    private static void AddCustomTagToSession(string tagName)
    {
        var session = AppConfig.CurrentSession;
        if (session == null) return;

        if (!session.CustomTags.Contains(tagName, StringComparer.OrdinalIgnoreCase))
        {
            session.CustomTags.Add(tagName);
            SaveSessionAsync(session);
        }
    }

    /// <summary>
    /// Removes a custom tag from the current session and saves it.
    /// </summary>
    private static void RemoveCustomTagFromSession(string tagName)
    {
        var session = AppConfig.CurrentSession;
        if (session == null) return;

        var index = session.CustomTags.FindIndex(t => t.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            session.CustomTags.RemoveAt(index);
            SaveSessionAsync(session);
        }
    }

    /// <summary>
    /// Saves the session asynchronously (fire and forget).
    /// </summary>
    private static async void SaveSessionAsync(Models.WorkshopSession session)
    {
        try
        {
            var settingsService = new SettingsService();
            var sessionRepository = new SessionRepository(settingsService);
            await sessionRepository.SaveSessionAsync(session);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to save session: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets file size in bytes.
    /// </summary>
    private static long GetFileSize(string? filePath)
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
    /// Formats file size for display.
    /// </summary>
    private static string FormatFileSize(string? filePath)
    {
        var size = GetFileSize(filePath);
        if (size == 0)
            return string.Empty;

        return size switch
        {
            < 1024 => $"{size} B",
            < 1024 * 1024 => $"{size / 1024.0:F1} KB",
            _ => $"{size / (1024.0 * 1024):F2} MB"
        };
    }

    /// <summary>
    /// Formats folder size for display.
    /// </summary>
    private static string FormatFolderSize(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return string.Empty;

        try
        {
            var dirInfo = new DirectoryInfo(folderPath);
            var size = dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

            return size switch
            {
                < 1024 => $"{size} B",
                < 1024 * 1024 => $"{size / 1024.0:F1} KB",
                < 1024 * 1024 * 1024 => $"{size / (1024.0 * 1024):F1} MB",
                _ => $"{size / (1024.0 * 1024 * 1024):F2} GB"
            };
        }
        catch
        {
            return string.Empty;
        }
    }
}
