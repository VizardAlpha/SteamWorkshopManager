using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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

public partial class CreateItemViewModel : ViewModelBase
{
    private readonly ISteamService _steamService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IProgress<UploadProgress>? _uploadProgress;
    private readonly DependencyService _dependencyService;
    private readonly AppDependencyService _appDependencyService;
    private readonly VersioningService _versioningService;
    private readonly DraftService _draftService;
    private readonly TagSelectionService _tagSelection;
    private readonly WorkshopOrchestrator _orchestrator;
    private readonly WorkshopTagsService _workshopTagsService;
    private readonly ISessionRepository _sessionRepository;

    /// <summary>
    /// If non-null, the user is editing a previously saved draft. Further
    /// <see cref="SaveAsDraftAsync"/> calls overwrite its folder, and a
    /// successful publish deletes it.
    /// </summary>
    private string? _currentDraftId;
    private DateTime? _draftCreatedAt;

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

    /// <summary>
    /// Dispose the previous bitmap before the setter replaces it, so the
    /// SkiaSharp native surface is released immediately instead of waiting on
    /// GC. Runs between every user preview change.
    /// </summary>
    partial void OnPreviewImageChanging(Bitmap? value) => _previewImage?.Dispose();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentFolderSize))]
    [NotifyPropertyChangedFor(nameof(IsInfoComplete))]
    private string? _contentFolderPath;

    public string ContentFolderSize
    {
        get
        {
            var size = ModFileInfoBuilder.InspectFolder(ContentFolderPath).Size;
            return size > 0 ? Formatters.Bytes(size) : string.Empty;
        }
    }

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
    private bool _isDragOver;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _newCustomTag = string.Empty;

    [ObservableProperty]
    private string _newDependencyInput = "";

    [ObservableProperty]
    private DependencyInfo? _previewDependency;

    [ObservableProperty]
    private bool _isSearchingDependency;

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
    private string? _addAppError;

    public ObservableCollection<AppDependencyInfo> AppDependencies { get; } = [];

    // Versioning
    [ObservableProperty]
    private bool _isVersioningEnabled;

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
    /// Section-ready flags for the left nav's green check icon. A section
    /// gets a check once it has no blocking validation error and enough data
    /// to submit. Optional sections stay at true as long as no explicit
    /// error is present.
    /// </summary>
    public bool IsInfoComplete => !string.IsNullOrWhiteSpace(Title)
                                  && !string.IsNullOrEmpty(ContentFolderPath)
                                  && !IsImageTooLarge;
    public bool IsVersionsComplete => !IsBranchRangeInvalid;
    public bool IsDependenciesComplete => true;

    public List<GameBranch> AvailableBranches { get; private set; } = [];

    [ObservableProperty]
    private bool _isRefreshingTags;

    [ObservableProperty]
    private string _tagsLastUpdatedText = "";

    [ObservableProperty]
    private bool _hasTags;

    public ObservableCollection<TagCategory> TagCategories { get; } = [];
    public ObservableCollection<WorkshopTag> CustomTags { get; } = [];

    /// <summary>
    /// Side-panel nav. Creation only has three meaningful sections:
    /// Info (source + form), Versions (changelog + branch range),
    /// Dependencies (mods + apps). History + Changelog-only views don't
    /// apply here so they're not exposed.
    /// </summary>
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

    /// <summary>Carousel sub-lists; mirrors the Workshop UI's per-type
    /// sections. Everything is "new" since there's nothing on Steam yet.</summary>
    public ObservableCollection<WorkshopPreview> ImagePreviews { get; } = [];
    public ObservableCollection<WorkshopPreview> VideoPreviews { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYouTubeInputValid))]
    private string _newYouTubeInput = string.Empty;

    [ObservableProperty]
    private string? _previewError;

    public bool IsYouTubeInputValid => !string.IsNullOrWhiteSpace(ItemEditorViewModel.ParseYouTubeId(NewYouTubeInput));

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
        var id = ItemEditorViewModel.ParseYouTubeId(NewYouTubeInput);
        if (string.IsNullOrEmpty(id))
        {
            PreviewError = Loc["InvalidYouTubeInput"];
            return;
        }
        if (VideoPreviews.Any(p => p.VideoId == id))
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

    private ObservableCollection<WorkshopPreview>? FindContainingList(WorkshopPreview p)
    {
        if (ImagePreviews.Contains(p)) return ImagePreviews;
        if (VideoPreviews.Contains(p)) return VideoPreviews;
        return null;
    }


    /// <summary>
    /// Saved drafts for the current AppId, ordered by most recently updated.
    /// Rebuilt each time <see cref="RefreshDrafts"/> runs (construction + after
    /// save / delete), so the sidebar flyout always reflects disk state.
    /// </summary>
    public ObservableCollection<CreateDraft> AvailableDrafts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDrafts))]
    private int _draftsCount;

    public bool HasDrafts => DraftsCount > 0;

    private void RefreshDrafts()
    {
        AvailableDrafts.Clear();
        foreach (var d in _draftService.ListForApp(AppConfig.AppId))
            AvailableDrafts.Add(d);
        DraftsCount = AvailableDrafts.Count;
    }

    [RelayCommand]
    private void SaveAsDraft()
    {
        var createdAt = _draftCreatedAt ?? DateTime.UtcNow;

        // Persist every user-added custom tag (whether checked or not) so the
        // chip list survives a reload, and the flat list of currently-checked
        // names across both sources so ticks can be restored.
        var customNames = CustomTags.Select(t => t.Name).ToList();
        var selectedNames = TagSelectionService.CollectSelectedNames(TagCategories, CustomTags);

        var draft = new CreateDraft(
            TempId: _currentDraftId ?? string.Empty,
            AppId: AppConfig.AppId,
            Title: Title,
            Description: Description,
            ContentFolderPath: ContentFolderPath,
            PreviewImagePath: PreviewImagePath,
            Visibility: Visibility,
            InitialChangelog: InitialChangelog,
            TargetAllVersions: TargetAllVersions,
            BranchMin: SelectedBranchMin?.Name,
            BranchMax: SelectedBranchMax?.Name,
            CreatedAt: createdAt,
            UpdatedAt: DateTime.UtcNow,
            CustomTags: customNames,
            SelectedTags: selectedNames);

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
        TargetAllVersions = draft.TargetAllVersions;
        SelectedBranchMin = AvailableBranches.FirstOrDefault(b => b.Name == draft.BranchMin);
        SelectedBranchMax = AvailableBranches.FirstOrDefault(b => b.Name == draft.BranchMax);

        TagSelectionService.RestoreFromDraft(draft, TagCategories, CustomTags);

        _currentDraftId = draft.TempId;
        _draftCreatedAt = draft.CreatedAt;

        // Reload preview bitmap from disk if the path is still valid.
        if (!string.IsNullOrEmpty(PreviewImagePath) && File.Exists(PreviewImagePath))
        {
            try { PreviewImage = new Bitmap(PreviewImagePath); }
            catch { /* path may be on another machine — ignore */ }
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

    public static IEnumerable<VisibilityType> VisibilityOptions =>
        Enum.GetValues<VisibilityType>();
    
    public event Action<PublishedFileId_t>? ItemCreated;

    public CreateItemViewModel(
        ISteamService steamService,
        IFileDialogService fileDialogService,
        ISettingsService settingsService,
        INotificationService notificationService,
        DependencyService dependencyService,
        AppDependencyService appDependencyService,
        VersioningService versioningService,
        DraftService draftService,
        TagSelectionService tagSelection,
        WorkshopOrchestrator orchestrator,
        WorkshopTagsService workshopTagsService,
        ISessionRepository sessionRepository,
        IProgress<UploadProgress>? uploadProgress = null)
    {
        _steamService = steamService;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _dependencyService = dependencyService;
        _appDependencyService = appDependencyService;
        _versioningService = versioningService;
        _draftService = draftService;
        _tagSelection = tagSelection;
        _orchestrator = orchestrator;
        _workshopTagsService = workshopTagsService;
        _sessionRepository = sessionRepository;
        _uploadProgress = uploadProgress;

        RefreshDrafts();
        ReloadVersioningFromCurrentSession();
        ReloadTagsFromCurrentSession();
        UpdateTagsLastUpdatedText();
    }

    /// <summary>
    /// Called by <c>MainViewModel</c> after a session switch so the Create
    /// form can re-derive its catalog (tags, branches, drafts) from the new
    /// session without being rebuilt from scratch. Any draft currently being
    /// edited is discarded since drafts are scoped per-AppId.
    /// </summary>
    public void OnSessionChanged()
    {
        _currentDraftId = null;
        _draftCreatedAt = null;

        RefreshDrafts();
        ReloadVersioningFromCurrentSession();
        ReloadTagsFromCurrentSession();
        UpdateTagsLastUpdatedText();
    }

    private void ReloadVersioningFromCurrentSession()
    {
        IsVersioningEnabled = _versioningService.IsVersioningEnabled();
        if (IsVersioningEnabled)
        {
            CurrentBranch = _versioningService.GetCurrentBranch();
            AvailableBranches = _versioningService.GetAvailableBranches();
        }
        else
        {
            CurrentBranch = string.Empty;
            AvailableBranches = [];
        }
        SelectedBranchMin = null;
        SelectedBranchMax = null;
        TargetAllVersions = true;
        IsBranchRangeInvalid = false;
        OnPropertyChanged(nameof(AvailableBranches));
    }

    private void ReloadTagsFromCurrentSession()
    {
        TagCategories.Clear();
        CustomTags.Clear();

        var sessionTags = AppConfig.CurrentSession?.TagsByCategory ?? new Dictionary<string, List<string>>();
        var dropdownCategories = AppConfig.CurrentSession?.DropdownCategories ?? [];
        foreach (var (category, tags) in sessionTags)
        {
            var tagCategory = new TagCategory
            {
                Name = category,
                IsDropdown = dropdownCategories.Contains(category),
            };
            foreach (var tag in tags)
                tagCategory.Tags.Add(new WorkshopTag(tag, false));
            tagCategory.SyncSelectedTag();
            TagCategories.Add(tagCategory);
        }
        HasTags = TagCategories.Count > 0;

        foreach (var customTag in AppConfig.CurrentSession?.CustomTags ?? [])
            CustomTags.Add(new WorkshopTag(customTag, false));
    }

    private void UpdateTagsLastUpdatedText()
    {
        var lastUpdated = AppConfig.CurrentSession?.TagsLastUpdated;
        TagsLastUpdatedText = lastUpdated is { Year: > 2000 }
            ? $"{LocalizationService.GetString("TagsUpdated")} {lastUpdated.Value.ToLocalTime():g}"
            : "";
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
            string? branchMin = null, branchMax = null;
            if (IsVersioningEnabled && !TargetAllVersions)
            {
                branchMin = SelectedBranchMin?.Name;
                branchMax = SelectedBranchMax?.Name;
            }

            var previewOps = PreviewOpBuilder.BuildForCreate(ImagePreviews, VideoPreviews);

            var request = new CreateModRequest(
                Title,
                Description,
                ContentFolderPath!,
                PreviewImagePath,
                Visibility,
                TagSelectionService.CollectSelectedNames(TagCategories, CustomTags),
                InitialChangelog,
                branchMin,
                branchMax,
                previewOps.Count > 0 ? previewOps : null,
                Dependencies,
                AppDependencies);

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

    [RelayCommand]
    private async Task SearchDependencyAsync()
    {
        DependencyError = null;
        PreviewDependency = null;

        var fileId = ItemEditorViewModel.ParseWorkshopInput(NewDependencyInput);
        if (fileId == 0)
        {
            DependencyError = Loc["InvalidWorkshopInput"];
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
    private void ConfirmAddDependency()
    {
        if (PreviewDependency == null) return;

        Dependencies.Add(PreviewDependency);
        PreviewDependency = null;
        NewDependencyInput = "";
        DependencyError = null;
    }

    [RelayCommand]
    private void CancelDependencyPreview()
    {
        PreviewDependency = null;
        DependencyError = null;
    }

    [RelayCommand]
    private void RemoveDependency(DependencyInfo dep)
    {
        Dependencies.Remove(dep);
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
    private void ConfirmAddApp()
    {
        if (AppPreviewInfo == null) return;

        AppDependencies.Add(AppPreviewInfo);
        AppPreviewInfo = null;
        NewAppIdInput = "";
        AddAppError = null;
    }

    [RelayCommand]
    private void CancelAppPreview()
    {
        AppPreviewInfo = null;
        AddAppError = null;
    }

    [RelayCommand]
    private void RemoveAppDependency(AppDependencyInfo dep)
    {
        AppDependencies.Remove(dep);
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
}
