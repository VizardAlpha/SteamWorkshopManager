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
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using QRCoder;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Helpers;
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
    private readonly TagSelectionService _tagSelection;
    private readonly WorkshopOrchestrator _orchestrator;
    private readonly WorkshopTagsService _workshopTagsService;
    private readonly ISessionRepository _sessionRepository;
    private bool _changelogHistoryLoaded;
    private bool _dependenciesLoaded;
    private bool _versionsLoaded;
    private static readonly HttpClient HttpClient = new();

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

    public string PreviewImageSize
    {
        get
        {
            var size = ModFileInfoBuilder.InspectFile(PreviewImagePath).Size;
            return size > 0 ? Formatters.Bytes(size) : string.Empty;
        }
    }

    public bool IsImageTooLarge => ModValidator.IsImageTooLarge(PreviewImagePath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentFolderSize))]
    private string? _contentFolderPath;

    public string ContentFolderSize
    {
        get
        {
            var size = ModFileInfoBuilder.InspectFolder(ContentFolderPath).Size;
            return size > 0 ? Formatters.Bytes(size) : string.Empty;
        }
    }

    [ObservableProperty]
    private VisibilityType _visibility;

    [ObservableProperty]
    private string _newChangelog = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteConfirmed))]
    private bool _showDeleteConfirmation;

    /// <summary>
    /// Forces the user to type the shared <see cref="DangerousActions.ConfirmationPassphrase"/>
    /// before the destructive button enables — same pattern GitHub uses for
    /// repo deletion. Mirrors the bulk-delete and delete-session flows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteConfirmed))]
    private string _deleteTypedConfirmation = string.Empty;

    public bool IsDeleteConfirmed =>
        ShowDeleteConfirmation && string.Equals(DeleteTypedConfirmation, DangerousActions.ConfirmationPassphrase, StringComparison.Ordinal);

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isModIdCopied;

    public string ModId => ((ulong)_originalItem.PublishedFileId).ToString();

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
    [NotifyPropertyChangedFor(nameof(IsPreviewsActive))]
    private EditorSection _activeSection = EditorSection.Info;

    public bool IsInfoActive => ActiveSection == EditorSection.Info;
    public bool IsChangelogActive => ActiveSection == EditorSection.Changelog;
    public bool IsVersionsActive => ActiveSection == EditorSection.Versions;
    public bool IsHistoryActive => ActiveSection == EditorSection.History;
    public bool IsDependenciesActive => ActiveSection == EditorSection.Dependencies;
    public bool IsPreviewsActive => ActiveSection == EditorSection.Previews;

    [RelayCommand] private void NavigateToInfo() => ActiveSection = EditorSection.Info;
    [RelayCommand] private void NavigateToChangelog() => ActiveSection = EditorSection.Changelog;
    [RelayCommand] private void NavigateToVersions() => ActiveSection = EditorSection.Versions;
    [RelayCommand] private void NavigateToHistory() => ActiveSection = EditorSection.History;
    [RelayCommand] private void NavigateToDependencies() => ActiveSection = EditorSection.Dependencies;
    [RelayCommand] private void NavigateToPreviews() => ActiveSection = EditorSection.Previews;

    [RelayCommand]
    private async Task CopyModIdAsync()
    {
        await CopyToClipboardAsync(ModId);
        IsModIdCopied = true;
        try { await Task.Delay(TimeSpan.FromSeconds(2)); }
        finally { IsModIdCopied = false; }
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        }
        catch
        {
            // Clipboard failures are not actionable for the user
        }
    }

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

    /// <summary>Carousel sub-lists shown in the Workshop UI as separate
    /// sections; reorder is per-list, not interleaved.</summary>
    public ObservableCollection<WorkshopPreview> ImagePreviews { get; } = [];
    public ObservableCollection<WorkshopPreview> VideoPreviews { get; } = [];
    public ObservableCollection<WorkshopPreview> ModelPreviews { get; } = [];

    private readonly List<uint> _removedExistingIndices = [];

    /// <summary>
    /// Original Steam-side indices captured at load. Lets us issue
    /// <see cref="PreviewOp.Remove"/> for *all* existing previews when a
    /// reorder forces a full rebuild — the alternative would be to read the
    /// current Steam state again at save time, which we want to avoid.
    /// </summary>
    private readonly List<uint> _originalExistingIndices = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYouTubeInputValid))]
    private string _newYouTubeInput = string.Empty;

    [ObservableProperty]
    private string? _previewError;

    public bool IsYouTubeInputValid => !string.IsNullOrWhiteSpace(ParseYouTubeId(NewYouTubeInput));

    public static IEnumerable<VisibilityType> VisibilityOptions =>
        Enum.GetValues<VisibilityType>();


    public event Action<PublishedFileId_t>? ItemUpdated;
    public event Action? ItemDeleted;

    public ItemEditorViewModel(
        WorkshopItem item,
        ISteamService steamService,
        IFileDialogService fileDialogService,
        ISettingsService settingsService,
        INotificationService notificationService,
        ChangelogScraperService changelogScraper,
        WorkshopDownloadService workshopDownloader,
        DependencyService dependencyService,
        AppDependencyService appDependencyService,
        VersioningService versioningService,
        TagSelectionService tagSelection,
        WorkshopOrchestrator orchestrator,
        WorkshopTagsService workshopTagsService,
        ISessionRepository sessionRepository,
        IProgress<UploadProgress>? uploadProgress = null)
    {
        _originalItem = item;
        _steamService = steamService;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _changelogScraper = changelogScraper;
        _workshopDownloader = workshopDownloader;
        _dependencyService = dependencyService;
        _appDependencyService = appDependencyService;
        _versioningService = versioningService;
        _tagSelection = tagSelection;
        _orchestrator = orchestrator;
        _workshopTagsService = workshopTagsService;
        _sessionRepository = sessionRepository;
        _uploadProgress = uploadProgress;

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
                _tagSelection.AddCustomTagToSession(tagName);
                CustomTags.Add(new WorkshopTag(tagName, true));
            }
        }

        // Init tags last updated text
        UpdateTagsLastUpdatedText();

        // Load preview image
        LoadPreviewImageAsync(item.PreviewImageUrl);

        // Split existing previews into the three Workshop sections (images,
        // videos, 3D models). New entries are appended to the matching list.
        foreach (var preview in item.AdditionalPreviews)
        {
            ListFor(preview).Add(preview);
            _originalExistingIndices.Add(preview.OriginalIndex);
            if (preview.IsImage && !string.IsNullOrEmpty(preview.RemoteUrl))
                LoadPreviewThumbnailAsync(preview);
        }
    }

    private ObservableCollection<WorkshopPreview> ListFor(WorkshopPreview p)
    {
        if (p.IsVideo) return VideoPreviews;
        if (p.IsSketchfab) return ModelPreviews;
        return ImagePreviews;
    }

    private ObservableCollection<WorkshopPreview>? FindContainingList(WorkshopPreview p)
    {
        if (ImagePreviews.Contains(p)) return ImagePreviews;
        if (VideoPreviews.Contains(p)) return VideoPreviews;
        if (ModelPreviews.Contains(p)) return ModelPreviews;
        return null;
    }

    private async void LoadPreviewThumbnailAsync(WorkshopPreview preview)
    {
        try
        {
            var response = await HttpClient.GetAsync(preview.RemoteUrl);
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync();
                preview.Thumbnail = new Bitmap(stream);
            }
        }
        catch
        {
            // Ignore preview thumbnail failures
        }
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
        var validation = ModValidator.ValidateForUpdate(Title);
        if (!validation.IsValid)
        {
            ErrorMessage = Loc[validation.ErrorKey!];
            return;
        }

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            var contentFolder = HasContentFolderChanged() ? ContentFolderPath : null;
            var previewImage = HasPreviewImageChanged() ? PreviewImagePath : null;

            string? branchMin = null, branchMax = null;
            if (IsVersioningEnabled && !TargetAllVersions && contentFolder != null)
            {
                branchMin = SelectedBranchMin?.Name;
                branchMax = SelectedBranchMax?.Name;
            }

            var previewOps = await BuildPreviewOpsAsync();

            var request = new UpdateModRequest(
                _originalItem.PublishedFileId,
                Title != _originalItem.Title ? Title : null,
                Description != _originalItem.Description ? Description : null,
                contentFolder,
                previewImage,
                Visibility != _originalItem.Visibility ? Visibility : null,
                TagSelectionService.CollectSelectedNames(TagCategories, CustomTags),
                string.IsNullOrWhiteSpace(NewChangelog) ? null : NewChangelog,
                branchMin,
                branchMax,
                previewOps.Count > 0 ? previewOps : null);

            var result = await _orchestrator.UpdateAsync(request, _uploadProgress);

            if (result.Success)
            {
                // Resync the local fingerprint baseline so the next "has changed"
                // check uses the just-uploaded state.
                if (!string.IsNullOrEmpty(ContentFolderPath))
                {
                    var fp = ModFileInfoBuilder.InspectFolder(ContentFolderPath);
                    _initialFolderSize = fp.Size;
                    _initialFolderModified = fp.LastModifiedUtc;
                }
                if (!string.IsNullOrEmpty(PreviewImagePath))
                {
                    var fp = ModFileInfoBuilder.InspectFile(PreviewImagePath);
                    _initialImageSize = fp.Size;
                    _initialImageModified = fp.LastModifiedUtc;
                }

                NewChangelog = string.Empty;
                ItemUpdated?.Invoke(_originalItem.PublishedFileId);
            }
            else
            {
                ErrorMessage = result.ExceptionMessage is null
                    ? Loc[result.ErrorKey ?? "UpdateFailed"]
                    : $"{Loc[result.ErrorKey ?? "UpdateFailed"]}: {result.ExceptionMessage}";
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void ShowDeleteDialog()
    {
        DeleteTypedConfirmation = string.Empty;
        ShowDeleteConfirmation = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirmation = false;
        DeleteTypedConfirmation = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (!IsDeleteConfirmed) return;

        IsDeleting = true;
        ErrorMessage = null;

        try
        {
            var result = await _orchestrator.DeleteAsync(_originalItem.PublishedFileId);

            if (result.Success)
            {
                ItemDeleted?.Invoke();
            }
            else
            {
                ErrorMessage = result.ExceptionMessage is null
                    ? Loc[result.ErrorKey ?? "DeleteFailed"]
                    : $"{Loc[result.ErrorKey ?? "DeleteFailed"]}: {result.ExceptionMessage}";
                ShowDeleteConfirmation = false;
            }
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
            // Refresh the access token if we have a stored refresh token but no
            // live access token — keeps "already downloaded" badges accurate
            // across restarts without forcing a fresh QR scan.
            if (!SteamAuthService.IsAuthenticated && SteamAuthService.HasRefreshToken)
                await SteamAuthService.TryRefreshAccessTokenAsync();

            // The auth banner is shown alongside the entries (not in place of
            // them) so the user can still see which versions they've already
            // pulled, even when the manifest_ids needed for fresh downloads
            // require authentication.
            NeedsSteamAuth = !SteamAuthService.IsAuthenticated;

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
    private async Task AddImagePreviewAsync()
    {
        PreviewError = null;
        var paths = await _fileDialogService.OpenFilesAsync(
            Loc["SelectPreviewImage"],
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
        );
        if (paths.Count == 0) return;

        foreach (var path in paths)
        {
            var preview = new WorkshopPreview
            {
                Source = WorkshopPreviewSource.NewImage,
                PreviewType = EItemPreviewType.k_EItemPreviewType_Image,
                LocalPath = path,
            };
            try { preview.Thumbnail = new Bitmap(path); } catch { /* ignore */ }
            ImagePreviews.Add(preview);
        }
    }

    [RelayCommand]
    private void AddYouTubeVideo()
    {
        PreviewError = null;
        var id = ParseYouTubeId(NewYouTubeInput);
        if (string.IsNullOrEmpty(id))
        {
            PreviewError = Loc["InvalidYouTubeInput"];
            return;
        }
        if (VideoPreviews.Any(p => p.VideoId == id || p.RemoteUrl == id))
        {
            PreviewError = Loc["YouTubeAlreadyAdded"];
            return;
        }

        VideoPreviews.Add(new WorkshopPreview
        {
            Source = WorkshopPreviewSource.NewVideo,
            PreviewType = EItemPreviewType.k_EItemPreviewType_YouTubeVideo,
            VideoId = id,
        });
        NewYouTubeInput = string.Empty;
    }

    [RelayCommand]
    private void RemovePreview(WorkshopPreview preview)
    {
        if (preview == null) return;
        if (preview.Source == WorkshopPreviewSource.Existing)
            _removedExistingIndices.Add(preview.OriginalIndex);

        preview.Thumbnail?.Dispose();
        FindContainingList(preview)?.Remove(preview);
    }

    [RelayCommand]
    private void MovePreviewUp(WorkshopPreview preview)
    {
        var list = FindContainingList(preview);
        if (list == null) return;
        var index = list.IndexOf(preview);
        if (index > 0) list.Move(index, index - 1);
    }

    [RelayCommand]
    private void MovePreviewDown(WorkshopPreview preview)
    {
        var list = FindContainingList(preview);
        if (list == null) return;
        var index = list.IndexOf(preview);
        if (index >= 0 && index < list.Count - 1) list.Move(index, index + 1);
    }

    /// <summary>
    /// Pulls the 11-character video ID out of a YouTube URL or returns the
    /// input itself when it already looks like a bare ID. Returns null when
    /// nothing matches the expected shape.
    /// </summary>
    internal static string? ParseYouTubeId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        if (Regex.IsMatch(input, @"^[A-Za-z0-9_-]{11}$"))
            return input;

        var v = Regex.Match(input, @"[?&]v=([A-Za-z0-9_-]{11})");
        if (v.Success) return v.Groups[1].Value;

        var shortLink = Regex.Match(input, @"youtu\.be/([A-Za-z0-9_-]{11})");
        if (shortLink.Success) return shortLink.Groups[1].Value;

        var embed = Regex.Match(input, @"youtube\.com/embed/([A-Za-z0-9_-]{11})");
        if (embed.Success) return embed.Groups[1].Value;

        return null;
    }

    /// <summary>
    /// Steam's UGC API has no "move" verb, so any per-list reorder forces a
    /// full rebuild. A list is "in order" when existing entries appear in
    /// OriginalIndex-ascending order with new entries appended at the end.
    /// Models are read-only so they're skipped.
    /// </summary>
    private bool IsReorderNeeded() =>
        IsListOutOfOrder(ImagePreviews) || IsListOutOfOrder(VideoPreviews);

    private static bool IsListOutOfOrder(IEnumerable<WorkshopPreview> list)
    {
        var seenNew = false;
        uint? lastIdx = null;
        foreach (var p in list)
        {
            if (p.Source == WorkshopPreviewSource.Existing)
            {
                if (seenNew) return true;
                if (lastIdx is { } prev && p.OriginalIndex < prev) return true;
                lastIdx = p.OriginalIndex;
            }
            else
            {
                seenNew = true;
            }
        }
        return false;
    }

    /// <summary>
    /// Builds the preview-op list passed to the Steam update RPC. Hits the
    /// fast path (single Remove/Add ops) when the user only added or removed
    /// entries; falls back to a full rebuild — including downloading any
    /// existing image we still want to keep — when the in-list order no
    /// longer matches what Steam will hold after a naive replay.
    /// </summary>
    private async Task<List<PreviewOp>> BuildPreviewOpsAsync()
    {
        if (IsReorderNeeded())
            return await BuildRebuildOpsAsync();

        var ops = new List<PreviewOp>();
        foreach (var idx in _removedExistingIndices)
            ops.Add(new PreviewOp.Remove(idx));
        foreach (var p in ImagePreviews) PreviewOpBuilder.AppendNewPreviewOp(ops, p);
        foreach (var p in VideoPreviews) PreviewOpBuilder.AppendNewPreviewOp(ops, p);
        return ops;
    }


    private async Task<List<PreviewOp>> BuildRebuildOpsAsync()
    {
        var ops = new List<PreviewOp>();

        // Sketchfabs the user kept stay on Steam untouched — the SDK doesn't
        // let us re-add them, so wiping them here would silently delete data.
        var preserve = ModelPreviews
            .Where(m => m.Source == WorkshopPreviewSource.Existing)
            .Select(m => m.OriginalIndex)
            .ToHashSet();

        foreach (var idx in _originalExistingIndices)
        {
            if (preserve.Contains(idx)) continue;
            ops.Add(new PreviewOp.Remove(idx));
        }

        var tempDir = AppPaths.TempPreviewDir();
        Directory.CreateDirectory(tempDir);

        foreach (var p in ImagePreviews)
        {
            var path = p.LocalPath;
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(p.RemoteUrl))
                path = await DownloadPreviewToTempAsync(p.RemoteUrl, tempDir);
            if (!string.IsNullOrEmpty(path))
                ops.Add(new PreviewOp.AddImage(path));
        }

        foreach (var p in VideoPreviews)
        {
            var id = p.Source == WorkshopPreviewSource.NewVideo ? p.VideoId : p.RemoteUrl;
            if (!string.IsNullOrEmpty(id))
                ops.Add(new PreviewOp.AddVideo(id));
        }

        return ops;
    }

    private static async Task<string?> DownloadPreviewToTempAsync(string url, string tempDir)
    {
        try
        {
            using var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            // Steam CDN URLs typically end in .png/.jpg; fall back to .jpg
            // when there's no extension we recognize.
            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";

            var path = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ext);
            await using var fs = File.Create(path);
            await response.Content.CopyToAsync(fs);
            return path;
        }
        catch
        {
            return null;
        }
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
            var selectedTags = TagSelectionService
                .CollectSelectedNames(TagCategories, CustomTags)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Fetch fresh tags from Steam
            var tagsResult = await _workshopTagsService.GetTagsForAppAsync(session.AppId, forceRefresh: true);

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
        _tagSelection.AddCustomTagToSession(tagName);
        CustomTags.Add(new WorkshopTag(tagName, true));
        NewCustomTag = string.Empty;
    }

    [RelayCommand]
    private void RemoveCustomTag(WorkshopTag tag)
    {
        if (tag == null) return;

        _tagSelection.RemoveCustomTagFromSession(tag.Name);
        CustomTags.Remove(tag);
    }


    /// <summary>Fire-and-forget session save; failures aren't user-actionable here.</summary>
    private async void SaveSessionAsync(Models.WorkshopSession session)
    {
        try { await _sessionRepository.SaveSessionAsync(session); }
        catch { /* fire-and-forget */ }
    }

    /// <summary>True if the content folder differs from the last uploaded
    /// fingerprint (path, size, or last-write date).</summary>
    private bool HasContentFolderChanged()
    {
        if (ContentFolderPath != _initialContentFolderPath) return true;
        if (string.IsNullOrEmpty(ContentFolderPath)) return false;
        var fp = ModFileInfoBuilder.InspectFolder(ContentFolderPath);
        return fp.Size != _initialFolderSize || fp.LastModifiedUtc != _initialFolderModified;
    }

    /// <summary>True if the preview image differs from the last uploaded
    /// fingerprint (path, size, or last-write date).</summary>
    private bool HasPreviewImageChanged()
    {
        if (PreviewImagePath != _initialPreviewImagePath) return true;
        if (string.IsNullOrEmpty(PreviewImagePath)) return false;
        var fp = ModFileInfoBuilder.InspectFile(PreviewImagePath);
        return fp.Size != _initialImageSize || fp.LastModifiedUtc != _initialImageModified;
    }
}
