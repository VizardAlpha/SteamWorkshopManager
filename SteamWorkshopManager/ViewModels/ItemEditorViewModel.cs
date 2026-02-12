using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services;
using SteamWorkshopManager.Services.Interfaces;
using Steamworks;

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
    private long _initialFolderSize;
    private DateTime _initialFolderModified;
    private readonly string? _initialPreviewImagePath;
    private long _initialImageSize;
    private DateTime _initialImageModified;
    private readonly ChangelogScraperService _changelogScraper;
    private readonly WorkshopDownloadService _workshopDownloader;
    private readonly DependencyService _dependencyService;
    private bool _changelogHistoryLoaded;
    private bool _dependenciesLoaded;
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

    [ObservableProperty]
    private bool _isLoadingChangelogs;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _needsSteamAuth;

    [ObservableProperty]
    private bool _isAuthenticating;

    [ObservableProperty]
    private Bitmap? _qrCodeImage;

    private CancellationTokenSource? _authCts;

    [ObservableProperty]
    private bool _isLoadingDependencies;

    [ObservableProperty]
    private string _newDependencyInput = "";

    [ObservableProperty]
    private DependencyInfo? _previewDependency;

    [ObservableProperty]
    private bool _isSearchingDependency;

    [ObservableProperty]
    private bool _isAddingDependency;

    [ObservableProperty]
    private string? _dependencyError;

    public ObservableCollection<DependencyInfo> Dependencies { get; } = [];

    public ObservableCollection<TagCategory> TagCategories { get; } = [];
    public ObservableCollection<WorkshopTag> CustomTags { get; } = [];
    public ObservableCollection<ItemVersion> Versions { get; } = [];
    public ObservableCollection<ChangeLogEntry> ChangeLogHistory { get; } = [];

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
        _changelogScraper = new ChangelogScraperService();
        _workshopDownloader = new WorkshopDownloadService();
        _dependencyService = new DependencyService();

        _title = item.Title;
        _description = item.Description;
        _visibility = item.Visibility;

        // Load saved preview image info from session (fallback to item's path)
        var savedImageInfo = settingsService.GetPreviewImageInfo((ulong)item.PublishedFileId);
        _previewImagePath = savedImageInfo?.Path ?? item.PreviewImagePath;
        _initialPreviewImagePath = _previewImagePath;
        _initialImageSize = savedImageInfo?.Size ?? 0;
        _initialImageModified = savedImageInfo?.LastModifiedUtc ?? DateTime.MinValue;

        // Load saved content folder info from session
        var savedFolderInfo = settingsService.GetContentFolderInfo((ulong)item.PublishedFileId);
        _contentFolderPath = savedFolderInfo?.Path;
        _initialContentFolderPath = _contentFolderPath;
        _initialFolderSize = savedFolderInfo?.Size ?? 0;
        _initialFolderModified = savedFolderInfo?.LastModifiedUtc ?? DateTime.MinValue;

        // Load tags by category from current session
        var selectedTagNames = item.Tags.Select(t => t.Name).ToHashSet();
        var sessionTags = AppConfig.CurrentSession?.TagsByCategory ?? new Dictionary<string, List<string>>();
        var allPredefinedTags = sessionTags
            .SelectMany(kvp => kvp.Value.Concat(kvp.Value.Select(v => $"{kvp.Key}: {v}")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (category, tags) in sessionTags)
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

        // Load custom tags from current session
        var sessionCustomTags = AppConfig.CurrentSession?.CustomTags ?? [];
        foreach (var customTag in sessionCustomTags)
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
                // This is a custom tag from the item, add it to session
                AddCustomTagToSession(tagName);
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
        if (string.IsNullOrEmpty(url))
            return;

        try
        {
            var response = await HttpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync();
                PreviewImage = new Bitmap(stream);
            }
        }
        catch
        {
            // Ignore image load failures
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
                // Save current state as the last uploaded state for change detection
                var fileId = (ulong)_originalItem.PublishedFileId;
                if (!string.IsNullOrEmpty(ContentFolderPath))
                {
                    var (folderSize, folderModified) = GetFolderInfo(ContentFolderPath);
                    _settingsService.SetContentFolderInfo(fileId, new ItemFileInfo
                    {
                        Path = ContentFolderPath, Size = folderSize, LastModifiedUtc = folderModified
                    });
                    _initialFolderSize = folderSize;
                    _initialFolderModified = folderModified;
                }
                if (!string.IsNullOrEmpty(PreviewImagePath))
                {
                    var (imgSize, imgModified) = GetFileInfo(PreviewImagePath);
                    _settingsService.SetPreviewImageInfo(fileId, new ItemFileInfo
                    {
                        Path = PreviewImagePath, Size = imgSize, LastModifiedUtc = imgModified
                    });
                    _initialImageSize = imgSize;
                    _initialImageModified = imgModified;
                }

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

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 3 && !_changelogHistoryLoaded)
            LoadChangelogHistoryCommand.Execute(null);
        if (value == 4 && !_dependenciesLoaded)
            LoadDependenciesCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LoadChangelogHistoryAsync()
    {
        IsLoadingChangelogs = true;
        try
        {
            // Try to refresh access token if we have a stored refresh token
            if (!SteamAuthService.IsAuthenticated && SteamAuthService.HasRefreshToken)
                await SteamAuthService.TryRefreshAccessTokenAsync();

            // Not authenticated → show auth banner, don't scrape
            if (!SteamAuthService.IsAuthenticated)
            {
                NeedsSteamAuth = true;
                _changelogHistoryLoaded = true;
                return;
            }

            NeedsSteamAuth = false;

            var entries = await _changelogScraper.GetChangeLogsAsync((ulong)_originalItem.PublishedFileId);
            ChangeLogHistory.Clear();
            foreach (var entry in entries)
            {
                entry.IsDownloaded = _workshopDownloader.IsVersionDownloaded(
                    AppConfig.AppId, _originalItem.Title, entry.Timestamp);
                ChangeLogHistory.Add(entry);
            }
            _changelogHistoryLoaded = true;
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"{Loc["DownloadFailed"]}: {ex.Message}");
        }
        finally
        {
            IsLoadingChangelogs = false;
        }
    }

    [RelayCommand]
    private async Task DownloadVersionAsync(ChangeLogEntry entry)
    {
        IsDownloading = true;
        DownloadProgress = 0;
        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            var path = await _workshopDownloader.DownloadVersionAsync(
                AppConfig.AppId, (ulong)_originalItem.PublishedFileId, _originalItem.Title, entry, progress);

            if (path != null)
            {
                entry.IsDownloaded = true;
                // Force UI refresh by replacing the entry
                var index = ChangeLogHistory.IndexOf(entry);
                if (index >= 0)
                {
                    ChangeLogHistory.RemoveAt(index);
                    ChangeLogHistory.Insert(index, entry);
                }
                _notificationService.ShowSuccess(Loc["DownloadComplete"]);
            }
            else
            {
                _notificationService.ShowError(Loc["DownloadFailed"]);
            }
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"{Loc["DownloadFailed"]}: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
        }
    }

    [RelayCommand]
    private void OpenVersionFolder(ChangeLogEntry entry)
    {
        _workshopDownloader.OpenVersionFolder(AppConfig.AppId, _originalItem.Title, entry.Timestamp);
    }

    [RelayCommand]
    private void OpenChangelogInBrowser()
    {
        var url = $"https://steamcommunity.com/sharedfiles/filedetails/changelog/{(ulong)_originalItem.PublishedFileId}";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    [RelayCommand]
    private async Task AuthenticateWithSteamAsync()
    {
        IsAuthenticating = true;
        _authCts = new CancellationTokenSource();

        void OnQrUrlChanged(string url)
        {
            try
            {
                var qrGenerator = new QRCodeGenerator();
                var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
                var qrCode = new PngByteQRCode(qrCodeData);
                var pngBytes = qrCode.GetGraphic(5, [255, 255, 255], [30, 35, 40]);

                using var stream = new MemoryStream(pngBytes);
                QrCodeImage = new Bitmap(stream);
            }
            catch
            {
                // Ignore QR code generation failures
            }
        }

        try
        {
            SteamAuthService.QrChallengeUrlChanged += OnQrUrlChanged;
            await SteamAuthService.BeginQrAuthAsync(_authCts.Token);

            _notificationService.ShowSuccess(Loc["SteamAuthSuccess"]);
            NeedsSteamAuth = false;
            IsAuthenticating = false;
            QrCodeImage = null;

            // Reload changelogs with authenticated session
            _changelogHistoryLoaded = false;
            await LoadChangelogHistoryAsync();
        }
        catch (OperationCanceledException)
        {
            // User cancelled
        }
        catch (Exception ex)
        {
            _notificationService.ShowError(ex.Message);
        }
        finally
        {
            SteamAuthService.QrChallengeUrlChanged -= OnQrUrlChanged;
            IsAuthenticating = false;
            QrCodeImage = null;
            _authCts = null;
        }
    }

    [RelayCommand]
    private void CancelAuth()
    {
        _authCts?.Cancel();
    }

    [RelayCommand]
    private async Task LoadDependenciesAsync()
    {
        IsLoadingDependencies = true;
        DependencyError = null;
        try
        {
            var deps = await _dependencyService.GetDependenciesAsync(_originalItem.PublishedFileId);
            Dependencies.Clear();
            foreach (var dep in deps)
                Dependencies.Add(dep);
            _dependenciesLoaded = true;
        }
        catch (Exception ex)
        {
            DependencyError = ex.Message;
        }
        finally
        {
            IsLoadingDependencies = false;
        }
    }

    [RelayCommand]
    private async Task SearchDependencyAsync()
    {
        DependencyError = null;
        PreviewDependency = null;

        var fileId = ParseWorkshopInput(NewDependencyInput);
        if (fileId == 0)
        {
            DependencyError = Loc["InvalidWorkshopInput"];
            return;
        }

        // Check self-reference
        if (fileId == (ulong)_originalItem.PublishedFileId)
        {
            DependencyError = Loc["CannotAddSelf"];
            return;
        }

        // Check duplicate
        if (Dependencies.Any(d => d.PublishedFileId == fileId))
        {
            DependencyError = Loc["DependencyAlreadyExists"];
            return;
        }

        IsSearchingDependency = true;
        try
        {
            var info = await _dependencyService.GetModDetailsAsync(new PublishedFileId_t(fileId));
            if (info == null || !info.IsValid)
            {
                DependencyError = Loc["WorkshopItemNotFound"];
                return;
            }
            PreviewDependency = info;
        }
        catch (Exception ex)
        {
            DependencyError = ex.Message;
        }
        finally
        {
            IsSearchingDependency = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAddDependencyAsync()
    {
        if (PreviewDependency == null) return;

        IsAddingDependency = true;
        DependencyError = null;
        try
        {
            var childId = new PublishedFileId_t(PreviewDependency.PublishedFileId);
            var success = await _dependencyService.AddDependencyAsync(_originalItem.PublishedFileId, childId);
            if (success)
            {
                Dependencies.Add(PreviewDependency);
                PreviewDependency = null;
                NewDependencyInput = "";
                _notificationService.ShowSuccess(Loc["DependencyAdded"]);
            }
            else
            {
                DependencyError = Loc["AddDependencyFailed"];
            }
        }
        catch (Exception ex)
        {
            DependencyError = $"{Loc["AddDependencyFailed"]}: {ex.Message}";
        }
        finally
        {
            IsAddingDependency = false;
        }
    }

    [RelayCommand]
    private void CancelDependencyPreview()
    {
        PreviewDependency = null;
        DependencyError = null;
    }

    [RelayCommand]
    private async Task RemoveDependencyAsync(DependencyInfo dep)
    {
        if (dep == null) return;

        dep.IsRemoving = true;
        DependencyError = null;
        try
        {
            var childId = new PublishedFileId_t(dep.PublishedFileId);
            var success = await _dependencyService.RemoveDependencyAsync(_originalItem.PublishedFileId, childId);
            if (success)
            {
                Dependencies.Remove(dep);
                _notificationService.ShowSuccess(Loc["DependencyRemoved"]);
            }
            else
            {
                DependencyError = Loc["RemoveDependencyFailed"];
            }
        }
        catch (Exception ex)
        {
            DependencyError = $"{Loc["RemoveDependencyFailed"]}: {ex.Message}";
        }
        finally
        {
            dep.IsRemoving = false;
        }
    }

    [RelayCommand]
    private void MoveDependencyUp(DependencyInfo dep)
    {
        var index = Dependencies.IndexOf(dep);
        if (index > 0)
            Dependencies.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveDependencyDown(DependencyInfo dep)
    {
        var index = Dependencies.IndexOf(dep);
        if (index >= 0 && index < Dependencies.Count - 1)
            Dependencies.Move(index, index + 1);
    }

    [RelayCommand]
    private static void OpenDependencyInBrowser(DependencyInfo dep)
    {
        if (dep == null) return;
        Process.Start(new ProcessStartInfo { FileName = dep.WorkshopUrl, UseShellExecute = true });
    }

    internal static ulong ParseWorkshopInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return 0;

        input = input.Trim();

        // Try as raw numeric ID
        if (ulong.TryParse(input, out var rawId))
            return rawId;

        // Try to extract ?id= from URL
        var match = Regex.Match(input, @"[?&]id=(\d+)");
        if (match.Success && ulong.TryParse(match.Groups[1].Value, out var urlId))
            return urlId;

        return 0;
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
        catch
        {
            // Ignore session save failures in fire-and-forget
        }
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
