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
    private const long MaxImageSizeBytes = 1024 * 1024; // 1 MB Steam limit

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

    private List<PreviewOp> BuildPreviewOps()
    {
        var ops = new List<PreviewOp>();
        foreach (var p in ImagePreviews)
        {
            if (!string.IsNullOrEmpty(p.LocalPath))
                ops.Add(new PreviewOp.AddImage(p.LocalPath));
        }
        foreach (var p in VideoPreviews)
        {
            if (!string.IsNullOrEmpty(p.VideoId))
                ops.Add(new PreviewOp.AddVideo(p.VideoId));
        }
        return ops;
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
        var selectedNames = TagCategories
            .SelectMany(c => c.Tags)
            .Where(t => t.IsSelected)
            .Select(t => t.Name)
            .Concat(CustomTags.Where(t => t.IsSelected).Select(t => t.Name))
            .Distinct()
            .ToList();

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

        RestoreTagsFromDraft(draft);

        _currentDraftId = draft.TempId;
        _draftCreatedAt = draft.CreatedAt;

        // Reload preview bitmap from disk if the path is still valid.
        if (!string.IsNullOrEmpty(PreviewImagePath) && File.Exists(PreviewImagePath))
        {
            try { PreviewImage = new Bitmap(PreviewImagePath); }
            catch { /* path may be on another machine — ignore */ }
        }
    }

    /// <summary>
    /// Reapplies the draft's tag selection over the current tag model, without
    /// blowing away session-scoped custom tags the user has accumulated.
    /// Merges the draft's custom-tag names in (dedup by name), then resets
    /// every tag's IsSelected from the flat SelectedTags list. Selected names
    /// that match nothing known are promoted to new custom tags — so a draft
    /// survives a Steam category reshuffle.
    /// </summary>
    private void RestoreTagsFromDraft(CreateDraft draft)
    {
        // Merge: keep existing custom tags, add any draft-only ones.
        foreach (var name in draft.CustomTags ?? [])
        {
            if (!CustomTags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                CustomTags.Add(new WorkshopTag(name));
        }

        var selected = new HashSet<string>(draft.SelectedTags ?? [], StringComparer.OrdinalIgnoreCase);

        // Reset every known tag's checked state from the draft.
        foreach (var tag in TagCategories.SelectMany(c => c.Tags))
            tag.IsSelected = selected.Contains(tag.Name);
        foreach (var tag in CustomTags)
            tag.IsSelected = selected.Contains(tag.Name);

        // Any selected name that still has no home → custom tag, pre-checked.
        var knownNames = new HashSet<string>(
            TagCategories.SelectMany(c => c.Tags).Select(t => t.Name)
                .Concat(CustomTags.Select(t => t.Name)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in selected.Where(n => !knownNames.Contains(n)))
            CustomTags.Add(new WorkshopTag(name, isSelected: true));
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

    public CreateItemViewModel(ISteamService steamService, IFileDialogService fileDialogService,
        ISettingsService settingsService, INotificationService notificationService, IProgress<UploadProgress>? uploadProgress = null)
    {
        _steamService = steamService;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _uploadProgress = uploadProgress;
        _dependencyService = App.Services.GetRequiredService<DependencyService>();
        _appDependencyService = App.Services.GetRequiredService<AppDependencyService>();
        _versioningService = App.Services.GetRequiredService<VersioningService>();
        _draftService = App.Services.GetRequiredService<DraftService>();

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

            // Versioning: pass branch range if enabled and not targeting all
            string? branchMin = null, branchMax = null;
            if (IsVersioningEnabled && !TargetAllVersions)
            {
                branchMin = SelectedBranchMin?.Name;
                branchMax = SelectedBranchMax?.Name;
            }

            var previewOps = BuildPreviewOps();

            var fileId = await _steamService.CreateItemAsync(
                Title,
                Description,
                ContentFolderPath,
                PreviewImagePath,
                Visibility,
                selectedTags,
                string.IsNullOrWhiteSpace(InitialChangelog) ? "Initial version" : InitialChangelog,
                _uploadProgress,
                branchMin,
                branchMax,
                previewOps.Count > 0 ? previewOps : null
            );

            if (fileId.HasValue)
            {
                TelemetryService.Instance?.Track(TelemetryEventTypes.ModCreated, AppConfig.AppId);

                // Save paths + file info for change detection on future updates
                var id = (ulong)fileId.Value;
                if (!string.IsNullOrEmpty(ContentFolderPath) && Directory.Exists(ContentFolderPath))
                {
                    var dirInfo = new DirectoryInfo(ContentFolderPath);
                    var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
                    _settingsService.SetContentFolderInfo(id, new ItemFileInfo
                    {
                        Path = ContentFolderPath,
                        Size = files.Sum(f => f.Length),
                        LastModifiedUtc = files.Length > 0 ? files.Max(f => f.LastWriteTimeUtc) : dirInfo.LastWriteTimeUtc
                    });
                }
                if (!string.IsNullOrEmpty(PreviewImagePath) && File.Exists(PreviewImagePath))
                {
                    var fi = new FileInfo(PreviewImagePath);
                    _settingsService.SetPreviewImageInfo(id, new ItemFileInfo
                    {
                        Path = PreviewImagePath,
                        Size = fi.Length,
                        LastModifiedUtc = fi.LastWriteTimeUtc
                    });
                }

                // Add collected dependencies
                foreach (var dep in Dependencies)
                {
                    await _dependencyService.AddDependencyAsync(
                        fileId.Value, new PublishedFileId_t(dep.PublishedFileId));
                }

                // Add collected app dependencies
                foreach (var appDep in AppDependencies)
                {
                    await _appDependencyService.AddAppDependencyAsync(
                        fileId.Value, new AppId_t(appDep.AppId));
                }

                // Publish succeeded — discard the draft folder if this session
                // was editing one, so "Tempo/" stays clean.
                if (!string.IsNullOrEmpty(_currentDraftId))
                {
                    _draftService.Delete(_currentDraftId);
                    _currentDraftId = null;
                    _draftCreatedAt = null;
                    RefreshDrafts();
                }

                _notificationService.ShowSuccess(Loc["ItemCreatedSuccess"]);
                ItemCreated?.Invoke(fileId.Value);
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
