using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.UI;
using SteamWorkshopManager.ViewModels.Editor;
using Steamworks;

namespace SteamWorkshopManager.ViewModels;

/// <summary>
/// Create form. Owns the new item's own fields and drafts; tags, branches,
/// dependencies and gallery come from child view-models shared with the editor.
/// </summary>
public partial class CreateItemViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogService;
    private readonly INotificationService _notificationService;
    private readonly DraftService _draftService;
    private readonly WorkshopOrchestrator _orchestrator;
    private readonly ISessionContext _context;
    private readonly IProgress<UploadProgress>? _uploadProgress;

    /// <summary>
    /// If non-null, the user is editing a saved draft: saving overwrites its folder
    /// and a successful publish deletes it.
    /// </summary>
    private string? _currentDraftId;
    private DateTime? _draftCreatedAt;

    public TagEditorViewModel Tags { get; }
    public VersionRangeViewModel Versions { get; }
    public DependencyEditorViewModel Dependencies { get; }
    public PreviewGalleryViewModel Gallery { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInfoComplete))]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImageSize))]
    [NotifyPropertyChangedFor(nameof(IsImageTooLarge))]
    [NotifyPropertyChangedFor(nameof(IsInfoComplete))]
    private string? _previewImagePath;

    [ObservableProperty]
    private Bitmap? _previewImage;

    /// <summary>Release the previous bitmap's native surface right away instead of waiting on GC.</summary>
    partial void OnPreviewImageChanging(Bitmap? value) => _previewImage?.Dispose();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentFolderSize))]
    [NotifyPropertyChangedFor(nameof(IsInfoComplete))]
    private string? _contentFolderPath;

    // Folder scan runs off the UI thread; the getter reads the last result.
    private long _contentFolderBytes;
    private int _folderScanVersion;

    partial void OnContentFolderPathChanged(string? value) => _ = RefreshFolderSizeAsync(value);

    private async Task RefreshFolderSizeAsync(string? path)
    {
        var version = ++_folderScanVersion;
        _contentFolderBytes = 0;
        var size = await Task.Run(() => ModFileInfoBuilder.InspectFolder(path).Size);
        if (version != _folderScanVersion) return;

        _contentFolderBytes = size;
        OnPropertyChanged(nameof(ContentFolderSize));
    }

    public string ContentFolderSize =>
        _contentFolderBytes > 0 ? Formatters.Bytes(_contentFolderBytes) : string.Empty;

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
    private VisibilityType _visibility = VisibilityType.Private;

    [ObservableProperty]
    private string _initialChangelog = string.Empty;

    [ObservableProperty]
    private bool _isCreating;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Section-ready flags for the left nav's green check.</summary>
    public bool IsInfoComplete => !string.IsNullOrWhiteSpace(Title)
                                  && !string.IsNullOrEmpty(ContentFolderPath)
                                  && !IsImageTooLarge;
    public bool IsVersionsComplete => !Versions.IsBranchRangeInvalid;
    public bool IsDependenciesComplete => true;

    /// <summary>Side-panel nav: Info, Versions, Dependencies and Previews apply to a new item.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInfoActive))]
    [NotifyPropertyChangedFor(nameof(IsVersionsActive))]
    [NotifyPropertyChangedFor(nameof(IsDependenciesActive))]
    [NotifyPropertyChangedFor(nameof(IsPreviewsActive))]
    private EditorSection _activeSection = EditorSection.Info;

    public bool IsInfoActive => ActiveSection == EditorSection.Info;
    public bool IsVersionsActive => ActiveSection == EditorSection.Versions;
    public bool IsDependenciesActive => ActiveSection == EditorSection.Dependencies;
    public bool IsPreviewsActive => ActiveSection == EditorSection.Previews;

    [RelayCommand] private void NavigateToInfo() => ActiveSection = EditorSection.Info;
    [RelayCommand] private void NavigateToVersions() => ActiveSection = EditorSection.Versions;
    [RelayCommand] private void NavigateToDependencies() => ActiveSection = EditorSection.Dependencies;
    [RelayCommand] private void NavigateToPreviews() => ActiveSection = EditorSection.Previews;

    /// <summary>Saved drafts for the current AppId, most recently updated first.</summary>
    public ObservableCollection<CreateDraft> AvailableDrafts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDrafts))]
    private int _draftsCount;

    public bool HasDrafts => DraftsCount > 0;

    public static IEnumerable<VisibilityType> VisibilityOptions => Enum.GetValues<VisibilityType>();

    public event Action<PublishedFileId_t>? ItemCreated;

    public CreateItemViewModel(
        IFileDialogService fileDialogService,
        INotificationService notificationService,
        DraftService draftService,
        WorkshopOrchestrator orchestrator,
        ISessionContext context,
        TagEditorViewModel tags,
        VersionRangeViewModel versions,
        DependencyEditorViewModel dependencies,
        PreviewGalleryViewModel gallery,
        IProgress<UploadProgress>? uploadProgress = null)
    {
        _fileDialogService = fileDialogService;
        _notificationService = notificationService;
        _draftService = draftService;
        _orchestrator = orchestrator;
        _context = context;
        _uploadProgress = uploadProgress;

        Tags = tags;
        Versions = versions;
        Dependencies = dependencies;
        Gallery = gallery;
        Versions.PropertyChanged += OnVersionsPropertyChanged;

        ReloadFromSession();
    }

    private void OnVersionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VersionRangeViewModel.IsBranchRangeInvalid))
            OnPropertyChanged(nameof(IsVersionsComplete));
    }

    /// <summary>
    /// Called by the shell after a session switch: re-derives tags, branches and drafts
    /// in place. The draft being edited is dropped since drafts are scoped per AppId.
    /// </summary>
    public void OnSessionChanged()
    {
        _currentDraftId = null;
        _draftCreatedAt = null;
        ReloadFromSession();
    }

    private void ReloadFromSession()
    {
        RefreshDrafts();
        Versions.Reset();
        _ = Versions.LoadBranchesAsync();
        Tags.LoadFromSession();
    }

    private void RefreshDrafts()
    {
        AvailableDrafts.Clear();
        foreach (var d in _draftService.ListForApp(_context.AppId))
            AvailableDrafts.Add(d);
        DraftsCount = AvailableDrafts.Count;
    }

    [RelayCommand]
    private void SaveAsDraft()
    {
        var createdAt = _draftCreatedAt ?? DateTime.UtcNow;

        var draft = new CreateDraft(
            TempId: _currentDraftId ?? string.Empty,
            AppId: _context.AppId,
            Title: Title,
            Description: Description,
            ContentFolderPath: ContentFolderPath,
            PreviewImagePath: PreviewImagePath,
            Visibility: Visibility,
            InitialChangelog: InitialChangelog,
            TargetAllVersions: Versions.TargetAllVersions,
            BranchMin: Versions.SelectedBranchMin?.Name,
            BranchMax: Versions.SelectedBranchMax?.Name,
            CreatedAt: createdAt,
            UpdatedAt: DateTime.UtcNow,
            // Every custom tag (checked or not) so the chip list survives a reload, plus the ticked names.
            CustomTags: Tags.CustomTags.Select(t => t.Name).ToList(),
            SelectedTags: Tags.SelectedNames);

        _currentDraftId = _draftService.Save(draft);
        _draftCreatedAt = createdAt;
        _notificationService.ShowSuccess(Loc["DraftSaved"]);
        RefreshDrafts();
    }

    [RelayCommand]
    private void LoadDraft(CreateDraft draft)
    {
        Title = draft.Title;
        Description = draft.Description;
        ContentFolderPath = draft.ContentFolderPath;
        PreviewImagePath = draft.PreviewImagePath;
        Visibility = draft.Visibility;
        InitialChangelog = draft.InitialChangelog;
        Versions.TargetAllVersions = draft.TargetAllVersions;
        Versions.SelectByName(draft.BranchMin, draft.BranchMax);
        Tags.RestoreFromDraft(draft);

        _currentDraftId = draft.TempId;
        _draftCreatedAt = draft.CreatedAt;

        if (!string.IsNullOrEmpty(PreviewImagePath) && File.Exists(PreviewImagePath))
        {
            try { PreviewImage = new Bitmap(PreviewImagePath); }
            catch { /* path may be on another machine - ignore */ }
        }
    }

    [RelayCommand]
    private void DeleteDraft(CreateDraft draft)
    {
        _draftService.Delete(draft.TempId);
        if (_currentDraftId == draft.TempId)
        {
            _currentDraftId = null;
            _draftCreatedAt = null;
        }
        RefreshDrafts();
    }

    [RelayCommand]
    private async Task BrowsePreviewImageAsync()
    {
        var path = await _fileDialogService.OpenFileAsync(Loc["SelectPreviewImage"], WorkshopMedia.ImageExtensions);
        if (!string.IsNullOrEmpty(path)) SetPreviewImage(path);
    }

    private void SetPreviewImage(string path)
    {
        PreviewImagePath = path;
        try { PreviewImage = new Bitmap(path); }
        catch { /* unreadable image: the path is still used for the upload */ }
    }

    [RelayCommand]
    private void PreviewImageDropped(DropPayload payload)
    {
        if (payload.FirstPath is { } path) SetPreviewImage(path);
    }

    [RelayCommand]
    private async Task BrowseContentFolderAsync()
    {
        var path = await _fileDialogService.OpenFolderAsync(Loc["ContentFolder"]);
        if (!string.IsNullOrEmpty(path)) SetContentFolder(path);
    }

    private void SetContentFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        ContentFolderPath = path;

        if (string.IsNullOrEmpty(Title))
            Title = Path.GetFileName(path) ?? "New mod";
    }

    [RelayCommand]
    private void FolderDropped(DropPayload payload)
    {
        if (payload.FirstPath is { } path) SetContentFolder(path);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var validation = ModValidator.ValidateForCreate(Title, ContentFolderPath);
        if (!validation.IsValid)
        {
            ErrorMessage = Loc[validation.ErrorKey!];
            return;
        }

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var (branchMin, branchMax) = Versions.GetRange();
            var previewOps = Gallery.BuildOpsForCreate();

            var request = new CreateModRequest(
                Title,
                Description,
                ContentFolderPath!,
                PreviewImagePath,
                Visibility,
                Tags.SelectedNames,
                InitialChangelog,
                branchMin,
                branchMax,
                previewOps.Count > 0 ? previewOps : null,
                Dependencies.Dependencies,
                Dependencies.AppDependencies);

            var result = await _orchestrator.PublishAsync(request, _uploadProgress, _currentDraftId);

            if (result.Success && result.FileId.HasValue)
            {
                if (!string.IsNullOrEmpty(_currentDraftId))
                {
                    _currentDraftId = null;
                    _draftCreatedAt = null;
                    RefreshDrafts();
                }
                ItemCreated?.Invoke(result.FileId.Value);
            }
            else
            {
                ErrorMessage = result.ExceptionMessage is null
                    ? Loc[result.ErrorKey ?? "CreationFailed"]
                    : $"{Loc[result.ErrorKey ?? "CreationFailed"]}: {result.ExceptionMessage}";
            }
        }
        finally
        {
            IsCreating = false;
        }
    }
}
