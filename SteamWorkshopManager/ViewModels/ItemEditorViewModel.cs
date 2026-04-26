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
using Microsoft.Extensions.DependencyInjection;
using QRCoder;
using SteamWorkshopManager.Models;
using Steamworks;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Telemetry;
using SteamWorkshopManager.Services.UI;
using SteamWorkshopManager.Services.Workshop;

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
    private readonly AppDependencyService _appDependencyService;
    private readonly VersioningService _versioningService;
    private bool _changelogHistoryLoaded;
    private bool _dependenciesLoaded;
    private bool _versionsLoaded;
    private static readonly HttpClient HttpClient = new();
    private const long MaxImageSizeBytes = 1024 * 1024; // 1 MB Steam limit

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInfoComplete))]
    private string _title;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImageSize))]
    [NotifyPropertyChangedFor(nameof(IsImageTooLarge))]
    [NotifyPropertyChangedFor(nameof(IsInfoComplete))]
    private string? _previewImagePath;

    [ObservableProperty]
    private Bitmap? _previewImage;

    /// <summary>Release the previous preview's native surface before swapping.</summary>
    partial void OnPreviewImageChanging(Bitmap? value) => _previewImage?.Dispose();

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

    /// <summary>
    /// Side-panel nav: tracks which editor section is currently visible.
    /// OnActiveSectionChanged triggers lazy-loads for Versions / History /
    /// Dependencies the first time each one is opened.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInfoActive))]
    [NotifyPropertyChangedFor(nameof(IsChangelogActive))]
    [NotifyPropertyChangedFor(nameof(IsVersionsActive))]
    [NotifyPropertyChangedFor(nameof(IsHistoryActive))]
    [NotifyPropertyChangedFor(nameof(IsDependenciesActive))]
    private EditorSection _activeSection = EditorSection.Info;

    public bool IsInfoActive => ActiveSection == EditorSection.Info;
    public bool IsChangelogActive => ActiveSection == EditorSection.Changelog;
    public bool IsVersionsActive => ActiveSection == EditorSection.Versions;
    public bool IsHistoryActive => ActiveSection == EditorSection.History;
    public bool IsDependenciesActive => ActiveSection == EditorSection.Dependencies;

    [RelayCommand] private void NavigateToInfo() => ActiveSection = EditorSection.Info;
    [RelayCommand] private void NavigateToChangelog() => ActiveSection = EditorSection.Changelog;
    [RelayCommand] private void NavigateToVersions() => ActiveSection = EditorSection.Versions;
    [RelayCommand] private void NavigateToHistory() => ActiveSection = EditorSection.History;
    [RelayCommand] private void NavigateToDependencies() => ActiveSection = EditorSection.Dependencies;

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

    /// <summary>
    /// Release the previous QR bitmap. Steam rotates the challenge URL every
    /// few seconds while waiting for the scan; without this, every rotation
    /// would leak a bitmap for the duration of the auth session.
    /// </summary>
    partial void OnQrCodeImageChanging(Bitmap? value) => _qrCodeImage?.Dispose();

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

    // App dependencies
    [ObservableProperty]
    private string _newAppIdInput = "";

    [ObservableProperty]
    private AppDependencyInfo? _appPreviewInfo;

    [ObservableProperty]
    private bool _isSearchingApp;

    [ObservableProperty]
    private bool _isAddingApp;

    [ObservableProperty]
    private string? _addAppError;

    public ObservableCollection<AppDependencyInfo> AppDependencies { get; } = [];

    // Versioning
    [ObservableProperty]
    private bool _isVersioningEnabled;

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    private string _currentBranch = "";

    [ObservableProperty]
    private bool _targetAllVersions = true;

    [ObservableProperty]
    private GameBranch? _selectedBranchMin;

    [ObservableProperty]
    private GameBranch? _selectedBranchMax;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVersionsComplete))]
    private bool _isBranchRangeInvalid;

    /// <summary>
    /// Section-ready flags driving the green check in the left nav.
    /// A section is "complete" when it has no blocking error and enough data
    /// to be submitted safely. Optional sections default to true so they get
    /// a check as long as the user hasn't introduced a validation error.
    /// </summary>
    public bool IsInfoComplete => !string.IsNullOrWhiteSpace(Title) && !IsImageTooLarge;
    public bool IsVersionsComplete => !IsBranchRangeInvalid;
    public bool IsDependenciesComplete => true;

    public List<GameBranch> AvailableBranches { get; private set; } = [];
    public ObservableCollection<ModVersionInfo> ExistingVersions { get; } = [];

    [ObservableProperty]
    private bool _isRefreshingTags;

    [ObservableProperty]
    private string _tagsLastUpdatedText = "";

    [ObservableProperty]
    private bool _hasTags;

    public ObservableCollection<TagCategory> TagCategories { get; } = [];
    public ObservableCollection<WorkshopTag> CustomTags { get; } = [];
    public ObservableCollection<ChangeLogEntry> ChangeLogHistory { get; } = [];

    public static IEnumerable<VisibilityType> VisibilityOptions =>
        Enum.GetValues<VisibilityType>();


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
        _changelogScraper = App.Services.GetRequiredService<ChangelogScraperService>();
        _workshopDownloader = App.Services.GetRequiredService<WorkshopDownloadService>();
        _dependencyService = App.Services.GetRequiredService<DependencyService>();
        _appDependencyService = App.Services.GetRequiredService<AppDependencyService>();
        _versioningService = App.Services.GetRequiredService<VersioningService>();

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
        var dropdownCategories = AppConfig.CurrentSession?.DropdownCategories ?? [];
        var allPredefinedTags = sessionTags
            .SelectMany(kvp => kvp.Value.Concat(kvp.Value.Select(v => $"{kvp.Key}: {v}")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (category, tags) in sessionTags)
        {
            var tagCategory = new TagCategory
            {
                Name = category,
                IsDropdown = dropdownCategories.Contains(category)
            };
            foreach (var tag in tags)
            {
                // Check if tag is selected (with or without category prefix)
                var isSelected = selectedTagNames.Contains(tag) || selectedTagNames.Contains($"{category}: {tag}");
                tagCategory.Tags.Add(new WorkshopTag(tag, isSelected));
            }
            tagCategory.SyncSelectedTag();
            TagCategories.Add(tagCategory);
        }
        HasTags = TagCategories.Count > 0;

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

        // Init tags last updated text
        UpdateTagsLastUpdatedText();

        // Load preview image
        LoadPreviewImageAsync(item.PreviewImageUrl);
    }

    private void UpdateTagsLastUpdatedText()
    {
        var lastUpdated = AppConfig.CurrentSession?.TagsLastUpdated;
        TagsLastUpdatedText = lastUpdated is { Year: > 2000 }
            ? $"{LocalizationService.GetString("TagsUpdated")} {lastUpdated.Value.ToLocalTime():g}"
            : "";
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

            // Versioning: only pass branch range if enabled, not targeting all, and content is being uploaded
            string? branchMin = null, branchMax = null;
            if (IsVersioningEnabled && !TargetAllVersions && contentFolder != null)
            {
                branchMin = SelectedBranchMin?.Name;
                branchMax = SelectedBranchMax?.Name;
            }

            var success = await _steamService.UpdateItemAsync(
                _originalItem.PublishedFileId,
                Title != _originalItem.Title ? Title : null,
                Description != _originalItem.Description ? Description : null,
                contentFolder,
                previewImage,
                Visibility != _originalItem.Visibility ? Visibility : null,
                selectedTags,
                string.IsNullOrWhiteSpace(NewChangelog) ? null : NewChangelog,
                _uploadProgress,
                branchMin,
                branchMax
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
                TelemetryService.Instance?.Track(TelemetryEventTypes.ModUpdated, AppConfig.AppId);
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
                var fileId = (ulong)_originalItem.PublishedFileId;
                _settingsService.SetContentFolderInfo(fileId, null);
                _settingsService.SetPreviewImageInfo(fileId, null);

                TelemetryService.Instance?.Track(TelemetryEventTypes.ModDeleted, AppConfig.AppId);
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

    partial void OnActiveSectionChanged(EditorSection value)
    {
        switch (value)
        {
            case EditorSection.Versions when !_versionsLoaded:
                LoadVersionsCommand.Execute(null);
                break;
            case EditorSection.History when !_changelogHistoryLoaded:
                LoadChangelogHistoryCommand.Execute(null);
                break;
            case EditorSection.Dependencies when !_dependenciesLoaded:
                LoadDependenciesCommand.Execute(null);
                break;
        }
    }

    partial void OnSelectedBranchMinChanged(GameBranch? value) => ValidateBranchRange();
    partial void OnSelectedBranchMaxChanged(GameBranch? value) => ValidateBranchRange();

    private void ValidateBranchRange()
    {
        if (SelectedBranchMin == null || SelectedBranchMax == null)
        {
            IsBranchRangeInvalid = false;
            return;
        }
        var minIdx = AvailableBranches.IndexOf(SelectedBranchMin);
        var maxIdx = AvailableBranches.IndexOf(SelectedBranchMax);
        IsBranchRangeInvalid = minIdx > maxIdx;
    }

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        IsLoadingVersions = true;
        try
        {
            IsVersioningEnabled = _versioningService.IsVersioningEnabled();
            if (!IsVersioningEnabled)
            {
                _versionsLoaded = true;
                return;
            }

            CurrentBranch = _versioningService.GetCurrentBranch();
            AvailableBranches = _versioningService.GetAvailableBranches();
            OnPropertyChanged(nameof(AvailableBranches));

            var versions = await _versioningService.GetModVersionsAsync(_originalItem.PublishedFileId);
            ExistingVersions.Clear();
            foreach (var v in versions)
                ExistingVersions.Add(v);

            _versionsLoaded = true;
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"{Loc["LoadVersionsFailed"]}: {ex.Message}");
        }
        finally
        {
            IsLoadingVersions = false;
        }
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
            _authCts?.Dispose();
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

            var appDeps = await _appDependencyService.GetAppDependenciesAsync(_originalItem.PublishedFileId);
            AppDependencies.Clear();
            foreach (var appDep in appDeps)
                AppDependencies.Add(appDep);

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

    // App dependency commands

    [RelayCommand]
    private async Task SearchAppAsync()
    {
        AddAppError = null;
        AppPreviewInfo = null;

        if (!AppIdValidator.TryParseAppId(NewAppIdInput, out var appId))
        {
            AddAppError = Loc["InvalidAppId"];
            return;
        }

        // Block adding the current game itself
        if (appId == AppConfig.AppId)
        {
            AddAppError = Loc["CannotAddOwnGame"];
            return;
        }

        if (AppDependencies.Any(d => d.AppId == appId))
        {
            AddAppError = Loc["AppDependencyAlreadyExists"];
            return;
        }

        IsSearchingApp = true;
        try
        {
            var name = await _appDependencyService.ResolveAppNameAsync(appId);
            if (name == null)
            {
                AddAppError = Loc["AppNotFound"];
                return;
            }
            AppPreviewInfo = new AppDependencyInfo { AppId = appId, Name = name };
        }
        catch (Exception ex)
        {
            AddAppError = ex.Message;
        }
        finally
        {
            IsSearchingApp = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAddAppAsync()
    {
        if (AppPreviewInfo == null) return;

        IsAddingApp = true;
        AddAppError = null;
        try
        {
            var success = await _appDependencyService.AddAppDependencyAsync(
                _originalItem.PublishedFileId, new AppId_t(AppPreviewInfo.AppId));
            if (success)
            {
                AppDependencies.Add(AppPreviewInfo);
                AppPreviewInfo = null;
                NewAppIdInput = "";
                _notificationService.ShowSuccess(Loc["AppDependencyAdded"]);
            }
            else
            {
                AddAppError = Loc["AddAppDependencyFailed"];
            }
        }
        catch (Exception ex)
        {
            AddAppError = $"{Loc["AddAppDependencyFailed"]}: {ex.Message}";
        }
        finally
        {
            IsAddingApp = false;
        }
    }

    [RelayCommand]
    private void CancelAppPreview()
    {
        AppPreviewInfo = null;
        AddAppError = null;
    }

    [RelayCommand]
    private async Task RemoveAppDependencyAsync(AppDependencyInfo dep)
    {
        if (dep == null) return;

        dep.IsRemoving = true;
        AddAppError = null;
        try
        {
            var success = await _appDependencyService.RemoveAppDependencyAsync(
                _originalItem.PublishedFileId, new AppId_t(dep.AppId));
            if (success)
            {
                AppDependencies.Remove(dep);
                _notificationService.ShowSuccess(Loc["AppDependencyRemoved"]);
            }
            else
            {
                AddAppError = Loc["RemoveAppDependencyFailed"];
            }
        }
        catch (Exception ex)
        {
            AddAppError = $"{Loc["RemoveAppDependencyFailed"]}: {ex.Message}";
        }
        finally
        {
            dep.IsRemoving = false;
        }
    }

    [RelayCommand]
    private static void OpenAppInStore(AppDependencyInfo dep)
    {
        if (dep == null) return;
        Process.Start(new ProcessStartInfo { FileName = dep.StoreUrl, UseShellExecute = true });
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
    private async Task RefreshTagsAsync()
    {
        var session = AppConfig.CurrentSession;
        if (session == null) return;

        IsRefreshingTags = true;
        try
        {
            // Remember currently selected tags
            var selectedTags = TagCategories
                .SelectMany(c => c.Tags)
                .Where(t => t.IsSelected)
                .Select(t => t.Name)
                .Concat(CustomTags.Where(t => t.IsSelected).Select(t => t.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Fetch fresh tags from Steam
            var tagsService = App.Services.GetRequiredService<WorkshopTagsService>();
            var tagsResult = await tagsService.GetTagsForAppAsync(session.AppId, forceRefresh: true);

            // Update session
            session.TagsByCategory = tagsResult.TagsByCategory;
            session.DropdownCategories = tagsResult.DropdownCategories;
            session.TagsLastUpdated = DateTime.UtcNow;
            AppConfig.UpdateSession(session);
            SaveSessionAsync(session);

            // Rebuild UI
            TagCategories.Clear();
            foreach (var (category, tags) in tagsResult.TagsByCategory)
            {
                var tagCategory = new TagCategory
                {
                    Name = category,
                    IsDropdown = tagsResult.DropdownCategories.Contains(category)
                };
                foreach (var tag in tags)
                {
                    var isSelected = selectedTags.Contains(tag);
                    tagCategory.Tags.Add(new WorkshopTag(tag, isSelected));
                }
                tagCategory.SyncSelectedTag();
                TagCategories.Add(tagCategory);
            }
            HasTags = TagCategories.Count > 0;

            UpdateTagsLastUpdatedText();
        }
        finally
        {
            IsRefreshingTags = false;
        }
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
            var sessionRepository = App.Services.GetRequiredService<ISessionRepository>();
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
