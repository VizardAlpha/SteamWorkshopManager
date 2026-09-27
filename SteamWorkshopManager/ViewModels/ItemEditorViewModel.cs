using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.UI;
using SteamWorkshopManager.ViewModels.Editor;
using Steamworks;

namespace SteamWorkshopManager.ViewModels;

/// <summary>
/// Editor for a published item. Owns the item's own fields (title, description,
/// content, preview, visibility, changelog) and delegates tags, branches,
/// dependencies, gallery and history to child view-models shared with the create view.
/// </summary>
public partial class ItemEditorViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<ItemEditorViewModel>();
    private static readonly HttpClient Http = SteamHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(30));

    private readonly WorkshopItem _originalItem;
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly WorkshopOrchestrator _orchestrator;
    private readonly INotificationService _notificationService;
    private readonly IProgress<UploadProgress>? _uploadProgress;

    private readonly string? _initialContentFolderPath;
    private long _initialFolderSize;
    private DateTime _initialFolderModified;
    private readonly string? _initialPreviewImagePath;
    private long _initialImageSize;
    private DateTime _initialImageModified;

    private bool _versionsLoaded;

    public TagEditorViewModel Tags { get; }
    public VersionRangeViewModel Versions { get; }
    public DependencyEditorViewModel Dependencies { get; }
    public PreviewGalleryViewModel Gallery { get; }
    public ChangelogHistoryViewModel History { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInfoComplete))]
    private string _title;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImageSize))]
    [NotifyPropertyChangedFor(nameof(IsImageTooLarge))]
    [NotifyPropertyChangedFor(nameof(IsInfoComplete))]
    [NotifyPropertyChangedFor(nameof(IsImageSizeChanged))]
    [NotifyPropertyChangedFor(nameof(OriginalImageSizeDisplay))]
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
    [NotifyPropertyChangedFor(nameof(IsFolderSizeChanged))]
    [NotifyPropertyChangedFor(nameof(OriginalFolderSizeDisplay))]
    private string? _contentFolderPath;

    // Folder scans can walk thousands of files: done off the UI thread, getters read the cache.
    private FileFingerprint _folderFingerprint = FileFingerprint.Empty;
    private int _folderScanVersion;

    partial void OnContentFolderPathChanged(string? value) => _ = RefreshFolderFingerprintAsync();

    private async Task RefreshFolderFingerprintAsync()
    {
        var version = ++_folderScanVersion;
        var path = ContentFolderPath;
        var fp = await Task.Run(() => ModFileInfoBuilder.InspectFolder(path));
        if (version != _folderScanVersion) return;

        _folderFingerprint = fp;
        OnPropertyChanged(nameof(ContentFolderSize));
        OnPropertyChanged(nameof(IsFolderSizeChanged));
    }

    public string ContentFolderSize =>
        _folderFingerprint.Size > 0 ? Formatters.Bytes(_folderFingerprint.Size) : string.Empty;

    /// <summary>True when the folder changed since the last upload fingerprint: drives the size badge.</summary>
    public bool IsFolderSizeChanged => _initialFolderSize > 0 && HasContentFolderChanged();

    public string OriginalFolderSizeDisplay =>
        _initialFolderSize > 0 ? Formatters.Bytes(_initialFolderSize) : string.Empty;

    public bool IsImageSizeChanged => _initialImageSize > 0 && HasPreviewImageChanged();

    public string OriginalImageSizeDisplay =>
        _initialImageSize > 0 ? Formatters.Bytes(_initialImageSize) : string.Empty;

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

    /// <summary>The user types <see cref="DangerousActions.ConfirmationPassphrase"/> before deleting.</summary>
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

    /// <summary>Side-panel nav; the first visit of Versions / History / Dependencies triggers their load.</summary>
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

    /// <summary>Section-ready flags for the left nav's green check.</summary>
    public bool IsInfoComplete => !string.IsNullOrWhiteSpace(Title) && !IsImageTooLarge;
    public bool IsVersionsComplete => !Versions.IsBranchRangeInvalid;
    public bool IsDependenciesComplete => true;

    public static IEnumerable<VisibilityType> VisibilityOptions => Enum.GetValues<VisibilityType>();

    public event Action<PublishedFileId_t>? ItemUpdated;
    public event Action? ItemDeleted;

    public ItemEditorViewModel(
        WorkshopItem item,
        IFileDialogService fileDialogService,
        ISettingsService settingsService,
        WorkshopOrchestrator orchestrator,
        INotificationService notificationService,
        TagEditorViewModel tags,
        VersionRangeViewModel versions,
        DependencyEditorViewModel dependencies,
        PreviewGalleryViewModel gallery,
        ChangelogHistoryViewModel history,
        IProgress<UploadProgress>? uploadProgress = null)
    {
        _originalItem = item;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _orchestrator = orchestrator;
        _notificationService = notificationService;
        _uploadProgress = uploadProgress;

        Tags = tags;
        Versions = versions;
        Dependencies = dependencies;
        Gallery = gallery;
        History = history;

        _title = item.Title;
        _description = item.Description;
        _visibility = item.Visibility;

        // Saved fingerprints from the last upload, falling back to the item's own preview path.
        var savedImageInfo = settingsService.GetPreviewImageInfo((ulong)item.PublishedFileId);
        _previewImagePath = savedImageInfo?.Path ?? item.PreviewImagePath;
        _initialPreviewImagePath = _previewImagePath;
        _initialImageSize = savedImageInfo?.Size ?? 0;
        _initialImageModified = savedImageInfo?.LastModifiedUtc ?? DateTime.MinValue;

        var savedFolderInfo = settingsService.GetContentFolderInfo((ulong)item.PublishedFileId);
        _contentFolderPath = savedFolderInfo?.Path;
        _initialContentFolderPath = _contentFolderPath;
        _initialFolderSize = savedFolderInfo?.Size ?? 0;
        _initialFolderModified = savedFolderInfo?.LastModifiedUtc ?? DateTime.MinValue;
        _ = RefreshFolderFingerprintAsync();

        Tags.LoadFromSession(item.Tags.Select(t => t.Name).ToList(), adoptUnknown: true);
        Versions.PropertyChanged += OnVersionsPropertyChanged;
        Dependencies.AttachTo(item.PublishedFileId);
        Gallery.LoadExisting(item.AdditionalPreviews);
        History.AttachTo(item);

        _ = LoadPreviewImageAsync(item.PreviewImageUrl);
    }

    private void OnVersionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VersionRangeViewModel.IsBranchRangeInvalid))
            OnPropertyChanged(nameof(IsVersionsComplete));
    }

    partial void OnActiveSectionChanged(EditorSection value)
    {
        switch (value)
        {
            case EditorSection.Versions when !_versionsLoaded:
                LoadVersionsCommand.Execute(null);
                break;
            case EditorSection.History when !History.IsLoaded:
                History.LoadChangelogHistoryCommand.Execute(null);
                break;
            case EditorSection.Dependencies when !Dependencies.IsLoaded:
                Dependencies.LoadDependenciesCommand.Execute(null);
                break;
        }
    }

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        Versions.IsLoadingVersions = true;
        try
        {
            await Versions.LoadBranchesAsync();
            if (Versions.IsVersioningEnabled)
                await Versions.LoadExistingVersionsAsync(_originalItem.PublishedFileId);
            _versionsLoaded = true;
        }
        catch (Exception ex)
        {
            Log.Warning($"Loading versions failed: {ex.Message}");
            _notificationService.ShowError($"{Loc["LoadVersionsFailed"]}: {ex.Message}");
        }
        finally
        {
            Versions.IsLoadingVersions = false;
        }
    }

    [RelayCommand]
    private async Task CopyModIdAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(ModId);
            }
        }
        catch
        {
            // Clipboard failures are not actionable for the user
        }

        IsModIdCopied = true;
        try { await Task.Delay(TimeSpan.FromSeconds(2)); }
        finally { IsModIdCopied = false; }
    }

    private async Task LoadPreviewImageAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            // Decode off the UI thread; the continuation sets the property back on it.
            PreviewImage = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            });
        }
        catch (Exception ex)
        {
            Log.Debug($"Preview image load failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task BrowsePreviewImageAsync()
    {
        var path = await _fileDialogService.OpenFileAsync(Loc["SelectPreviewImage"], WorkshopMedia.ImageExtensions);
        if (string.IsNullOrEmpty(path)) return;

        PreviewImagePath = path;
        _settingsService.SetPreviewImagePath((ulong)_originalItem.PublishedFileId, path);
        try { PreviewImage = new Bitmap(path); }
        catch { /* unreadable image: the path is still used for the upload */ }
    }

    [RelayCommand]
    private async Task BrowseContentFolderAsync()
    {
        var path = await _fileDialogService.OpenFolderAsync(Loc["ContentFolder"]);
        if (string.IsNullOrEmpty(path)) return;

        ContentFolderPath = path;
        _settingsService.SetContentFolderPath((ulong)_originalItem.PublishedFileId, path);
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
            // Files may have changed since the folder was picked: rescan before diffing.
            await RefreshFolderFingerprintAsync();
            var contentFolder = HasContentFolderChanged() ? ContentFolderPath : null;
            var previewImage = HasPreviewImageChanged() ? PreviewImagePath : null;

            // A branch range only applies to new content.
            var (branchMin, branchMax) = contentFolder != null ? Versions.GetRange() : (null, null);

            var previewOps = await Gallery.BuildOpsForUpdateAsync();
            if (previewOps is null)
            {
                ErrorMessage = Loc["PreviewDownloadFailed"];
                return;
            }

            var request = new UpdateModRequest(
                _originalItem.PublishedFileId,
                Title != _originalItem.Title ? Title : null,
                Description != _originalItem.Description ? Description : null,
                contentFolder,
                previewImage,
                Visibility != _originalItem.Visibility ? Visibility : null,
                Tags.SelectedNames,
                string.IsNullOrWhiteSpace(NewChangelog) ? null : NewChangelog,
                branchMin,
                branchMax,
                previewOps.Count > 0 ? previewOps : null);

            var result = await _orchestrator.UpdateAsync(request, _uploadProgress);

            if (result.Success)
            {
                // New baseline for the next "has changed" check; the folder was rescanned before the upload.
                if (!string.IsNullOrEmpty(ContentFolderPath))
                {
                    _initialFolderSize = _folderFingerprint.Size;
                    _initialFolderModified = _folderFingerprint.LastModifiedUtc;
                }
                if (!string.IsNullOrEmpty(PreviewImagePath))
                {
                    var fp = ModFileInfoBuilder.InspectFile(PreviewImagePath);
                    _initialImageSize = fp.Size;
                    _initialImageModified = fp.LastModifiedUtc;
                }
                OnPropertyChanged(nameof(IsFolderSizeChanged));
                OnPropertyChanged(nameof(OriginalFolderSizeDisplay));
                OnPropertyChanged(nameof(IsImageSizeChanged));
                OnPropertyChanged(nameof(OriginalImageSizeDisplay));

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
            Gallery.CleanupTempFiles();
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

    /// <summary>True if the content folder differs from the last uploaded fingerprint (path, size or date).</summary>
    private bool HasContentFolderChanged()
    {
        if (ContentFolderPath != _initialContentFolderPath) return true;
        if (string.IsNullOrEmpty(ContentFolderPath)) return false;
        return _folderFingerprint.Size != _initialFolderSize || _folderFingerprint.LastModifiedUtc != _initialFolderModified;
    }

    /// <summary>True if the preview image differs from the last uploaded fingerprint (path, size or date).</summary>
    private bool HasPreviewImageChanged()
    {
        if (PreviewImagePath != _initialPreviewImagePath) return true;
        if (string.IsNullOrEmpty(PreviewImagePath)) return false;
        var fp = ModFileInfoBuilder.InspectFile(PreviewImagePath);
        return fp.Size != _initialImageSize || fp.LastModifiedUtc != _initialImageModified;
    }

    /// <summary>Called when the shell drops the editor: cancels pending sign-in / downloads.</summary>
    public void Dispose()
    {
        Versions.PropertyChanged -= OnVersionsPropertyChanged;
        History.Dispose();
    }
}
