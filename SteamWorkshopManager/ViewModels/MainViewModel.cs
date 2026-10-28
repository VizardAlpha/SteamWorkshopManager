using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Steamworks;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Presence;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.ViewModels.Shell;
using ShellTab = SteamWorkshopManager.Models.ShellTab;

namespace SteamWorkshopManager.ViewModels;

/// <summary>Shell: navigation between screens, session switching, worker lifecycle feedback.</summary>
public partial class MainViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<MainViewModel>();

    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly ISessionRepository _sessionRepository;
    private readonly SessionManager _sessionManager;
    private readonly SessionHost _sessionHost;
    private readonly IDiscordPresenceService _presence;
    private readonly ISessionContext _context;
    private readonly IViewModelFactory _viewModels;
    private readonly ICacheStore _cache;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    public string AppVersionDisplay => AppInfo.VersionWithPrefix;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeActive))]
    [NotifyPropertyChangedFor(nameof(IsMyModsActive))]
    [NotifyPropertyChangedFor(nameof(IsCreateActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private ShellTab _activeTab = ShellTab.Home;

    public bool IsHomeActive => ActiveTab == ShellTab.Home;
    public bool IsMyModsActive => ActiveTab == ShellTab.MyMods;
    public bool IsCreateActive => ActiveTab == ShellTab.Create;
    public bool IsSettingsActive => ActiveTab == ShellTab.Settings;

    public string ActiveGameName => _context.Current?.GameName ?? Loc["AppTitle"];

    /// <summary>Avatar letter of the session pill.</summary>
    public string ActiveGameInitial =>
        string.IsNullOrEmpty(_context.Current?.GameName)
            ? "?"
            : _context.Current.GameName![..1].ToUpperInvariant();

    public bool HasActiveSession => _context.Current is not null;

    public string WindowTitle => $"Steam Workshop Manager - {ActiveGameName}";

    [ObservableProperty]
    private WorkshopItem? _selectedItem;

    /// <summary>Drives the full-window overlay during the worker swap + item reload.</summary>
    [ObservableProperty]
    private bool _isSwitchingSession;

    [ObservableProperty]
    private bool _showCloseDuringUploadConfirmation;

    [ObservableProperty]
    private bool _isCacheFullBannerVisible;

    public string CacheFullBannerText => string.Format(Loc["CacheFullBanner"], Formatters.Bytes(_cache.LimitBytes ?? 0));

    public UpdateController Updates { get; }
    public ToastViewModel Toasts { get; }
    public UploadProgressViewModel Upload { get; }
    public ConnectionStatusViewModel Connection { get; }
    public SessionListViewModel SessionList { get; }
    public SessionDeletionViewModel SessionDeletion { get; }
    public ItemListViewModel ItemListViewModel { get; }
    public HomeViewModel HomeViewModel { get; }

    /// <summary>Raised when the user confirms quitting despite a running upload.</summary>
    public event Action? ForceCloseRequested;

    public event Action? OpenAddSessionWizard;

    public MainViewModel(
        ISettingsService settingsService,
        INotificationService notificationService,
        ISessionRepository sessionRepository,
        SessionManager sessionManager,
        SessionHost sessionHost,
        IDiscordPresenceService presence,
        UpdateController updates,
        ISessionContext context,
        IViewModelFactory viewModels,
        ICacheStore cache)
    {
        Updates = updates;
        _context = context;
        _viewModels = viewModels;
        _cache = cache;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _sessionRepository = sessionRepository;
        _sessionManager = sessionManager;
        _sessionHost = sessionHost;
        _presence = presence;

        // Before any child view-model reads a string.
        LocalizationService.Instance.CurrentLanguage = _settingsService.Settings.Language;
        LocalizationService.Instance.Initialize();

        SteamAuthService.Initialize(_settingsService);

        Toasts = _viewModels.Create<ToastViewModel>();
        Upload = _viewModels.Create<UploadProgressViewModel>();
        Upload.InProgressChanged += OnUploadInProgressChanged;
        Connection = _viewModels.Create<ConnectionStatusViewModel>();
        SessionList = _viewModels.Create<SessionListViewModel>();
        SessionDeletion = _viewModels.Create<SessionDeletionViewModel>(SessionList, (Func<WorkshopSession, Task>)LeaveSessionAsync);

        ItemListViewModel = _viewModels.Create<ItemListViewModel>(_notificationService);
        ItemListViewModel.ItemSelected += OpenEditor;
        ItemListViewModel.CreateRequested += OpenCreator;
        ItemListViewModel.ItemsReconciled += OnItemsReconciled;

        HomeViewModel = _viewModels.Create<HomeViewModel>(ItemListViewModel);
        Connection.StateChanged += state => HomeViewModel.ConnectionState = state;

        _sessionHost.WorkerRecovered += OnWorkerRecovered;
        _sessionHost.WorkerUnrecoverable += OnWorkerUnrecoverable;

        CurrentView = HomeViewModel;
        ActiveTab = ShellTab.Home;

        _presence.Sync();

        _cache.LimitReached += OnCacheLimitReached;
        _cache.Changed += OnCacheChanged;
        LocalizationService.Instance.LanguageChanged += () => OnPropertyChanged(nameof(CacheFullBannerText));

        _ = ApplyInitialConnectionAsync();
        _ = CheckForUpdatesAsync();
        _ = SessionList.LoadAsync();
    }

    #region Steam connection

    /// <summary>The worker is started before the window: reflect its init result and load the items.</summary>
    private async Task ApplyInitialConnectionAsync()
    {
        try
        {
            var result = _sessionHost.LastInitResult;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                Connection.Apply(result);
                switch (result)
                {
                    case SteamInitResult.Success:
                        await ItemListViewModel.LoadItemsAsync();
                        break;
                    case SteamInitResult.GameNotOwned:
                        _notificationService.ShowError(Loc["GameNotOwnedMessage"]);
                        break;
                    case SteamInitResult.SteamOffline:
                        // No item load: the query would come back empty and read as "you have no mods".
                        _notificationService.ShowError(Loc["SteamOfflineMessage"]);
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error("Initial Steam state could not be applied", ex);
        }
    }

    // SessionHost raises both from the threadpool after Process.Exited.
    private void OnWorkerRecovered() => Dispatcher.UIThread.Post(() =>
    {
        // The dead worker never sent its final progress report.
        Upload.Abort();
        var result = _sessionHost.LastInitResult;
        Connection.Apply(result);
        _notificationService.ShowSuccess(Loc["WorkerRecovered"]);

        if (result == SteamInitResult.Success)
            _ = ItemListViewModel.LoadItemsAsync();
    });

    private void OnWorkerUnrecoverable() => Dispatcher.UIThread.Post(() =>
    {
        Upload.Abort();
        Connection.SetDisconnected();
        _notificationService.ShowError(Loc["WorkerUnrecoverable"]);
    });

    #endregion

    #region Navigation

    private void DetachCurrentView()
    {
        switch (CurrentView)
        {
            case ItemEditorViewModel editor:
                editor.ItemUpdated -= OnItemUpdated;
                editor.ItemDeleted -= OnItemDeleted;
                editor.Dispose();
                break;
            case CreateItemViewModel creator:
                creator.ItemCreated -= OnItemCreated;
                break;
            case SettingsViewModel settings:
                settings.Detach();
                break;
        }
    }

    private void Show(ViewModelBase view, ShellTab tab)
    {
        DetachCurrentView();
        CurrentView = view;
        ActiveTab = tab;
    }

    private void OpenEditor(WorkshopItem item)
    {
        DetachCurrentView();
        SelectedItem = item;
        var editor = _viewModels.Create<ItemEditorViewModel>(item, Upload.Reporter);
        editor.ItemUpdated += OnItemUpdated;
        editor.ItemDeleted += OnItemDeleted;
        CurrentView = editor;
        ActiveTab = ShellTab.MyMods;
    }

    private void OpenCreator()
    {
        DetachCurrentView();
        var creator = _viewModels.Create<CreateItemViewModel>(Upload.Reporter);
        creator.ItemCreated += OnItemCreated;
        CurrentView = creator;
    }

    /// <summary>Post-save: keep the editor mounted, refresh only the matching list row.</summary>
    private void OnItemUpdated(PublishedFileId_t fileId) => FireAndForget(async () =>
    {
        // Single-item fetch failed: best-effort full reload so the next list visit isn't stale.
        if (await ItemListViewModel.RefreshItemAsync(fileId) is null)
            await ItemListViewModel.LoadItemsAsync();
    }, "Post-save refresh");

    private void OnItemDeleted() => FireAndForget(async () =>
    {
        Show(ItemListViewModel, ShellTab.MyMods);
        SelectedItem = null;
        await ItemListViewModel.LoadItemsAsync();
    }, "Post-delete reload");

    /// <summary>
    /// Post-publish: fetch only the new item and open it in the editor. If Steam can't
    /// resolve the fresh id yet (indexing latency), fall back to a full list reload.
    /// </summary>
    private void OnItemCreated(PublishedFileId_t fileId) => FireAndForget(async () =>
    {
        DetachCurrentView();

        var item = await ItemListViewModel.RefreshItemAsync(fileId);
        if (item is not null)
        {
            OpenEditor(item);
            return;
        }

        _notificationService.ShowError(Loc["PostPublishFetchFailed"]);
        CurrentView = ItemListViewModel;
        ActiveTab = ShellTab.MyMods;
        await ItemListViewModel.LoadItemsAsync();
    }, "Post-publish navigation");

    /// <summary>Event handlers can't be awaited: log failures instead of letting them crash the app.</summary>
    private static async void FireAndForget(Func<Task> action, string operation)
    {
        try { await action(); }
        catch (Exception ex) { Log.Error($"{operation} failed", ex); }
    }

    [RelayCommand]
    private void NavigateToHome()
    {
        Show(HomeViewModel, ShellTab.Home);
        SelectedItem = null;
    }

    [RelayCommand]
    private void NavigateToList()
    {
        Show(ItemListViewModel, ShellTab.MyMods);
        SelectedItem = null;
    }

    [RelayCommand]
    private void NavigateToCreate()
    {
        OpenCreator();
        ActiveTab = ShellTab.Create;
    }

    [RelayCommand]
    private void NavigateToSettings() => Show(_viewModels.Create<SettingsViewModel>(), ShellTab.Settings);

    /// <summary>Ctrl+S: saves whichever editor is mounted, no-op anywhere else.</summary>
    [RelayCommand]
    private void SaveCurrent()
    {
        switch (CurrentView)
        {
            case ItemEditorViewModel editor when editor.SaveCommand.CanExecute(null):
                editor.SaveCommand.Execute(null);
                break;
            case CreateItemViewModel creator when creator.CreateCommand.CanExecute(null):
                creator.CreateCommand.Execute(null);
                break;
        }
    }

    #endregion

    #region Cache

    private void OnCacheLimitReached() => Dispatcher.UIThread.Post(() =>
    {
        OnPropertyChanged(nameof(CacheFullBannerText));
        IsCacheFullBannerVisible = true;
    });

    // A raised limit, a disabled or cleared cache resumes writes: the warning is moot.
    private void OnCacheChanged()
    {
        if (IsCacheFullBannerVisible && !_cache.IsFull) Dispatcher.UIThread.Post(() => IsCacheFullBannerVisible = false);
    }

    [RelayCommand]
    private void OpenStorageSettings()
    {
        NavigateToSettings();
        if (CurrentView is SettingsViewModel settings) settings.ActiveCategory = SettingsCategory.Storage;
        IsCacheFullBannerVisible = false;
    }

    [RelayCommand]
    private void DismissCacheBanner() => IsCacheFullBannerVisible = false;

    #endregion

    #region Steam refresh of the open mod

    // A refresh can bring a newer Steam version of the mod open in the editor.
    private void OnItemsReconciled(ItemsReconciliation outcome)
    {
        if (CurrentView is not ItemEditorViewModel editor || editor.IsSaving) return;

        var replaced = outcome.Replaced.FirstOrDefault(r => r.Old.PublishedFileId == editor.ItemId);
        if (replaced.Fresh is not null)
        {
            if (editor.HasUserEdits)
            {
                _notificationService.ShowError(Loc["ItemChangedOnSteam"]);
                return;
            }

            var section = editor.ActiveSection;
            OpenEditor(replaced.Fresh);
            if (CurrentView is ItemEditorViewModel reopened) reopened.ActiveSection = section;
            _notificationService.ShowSuccess(Loc["ItemUpdatedFromSteam"]);
            return;
        }

        if (outcome.Removed.Any(i => i.PublishedFileId == editor.ItemId))
        {
            _notificationService.ShowError(Loc["ItemRemovedOnSteam"]);
            if (!editor.HasUserEdits) NavigateToList();
        }
    }

    #endregion

    #region Upload

    private void OnUploadInProgressChanged(bool inProgress)
    {
        if (inProgress) Toasts.ApplyPosition();
        Updates.IsUploadInProgress = inProgress;
        RefreshPresence();
    }

    [RelayCommand]
    private void CancelCloseDuringUpload() => ShowCloseDuringUploadConfirmation = false;

    [RelayCommand]
    private void ConfirmCloseDuringUpload()
    {
        ShowCloseDuringUploadConfirmation = false;
        ForceCloseRequested?.Invoke();
    }

    #endregion

    #region Discord presence

    partial void OnActiveTabChanged(ShellTab value) => RefreshPresence();

    partial void OnCurrentViewChanged(ViewModelBase? value) => RefreshPresence();

    /// <summary>The service decides how much of this is published, based on the user's chosen mode.</summary>
    private void RefreshPresence() => _presence.Update(new PresenceState(
        ActiveTab,
        _context.Current?.GameName,
        _context.AppId,
        (CurrentView as ItemEditorViewModel)?.Title,
        Upload.IsInProgress));

    #endregion

    #region Sessions

    [RelayCommand]
    private async Task SwitchSessionAsync(WorkshopSession? session)
    {
        if (session is null || session.Id == _context.Current?.Id) return;

        Log.Debug($"Switching to session: {session.Name}");
        IsSwitchingSession = true;

        try
        {
            ResetSessionState();
            Connection.SetConnecting();

            var initResult = await _sessionManager.SwitchSessionAsync(session);

            SessionList.MarkActive(session.Id);
            Connection.Apply(initResult);
            NotifySessionChanged();

            switch (CurrentView)
            {
                case CreateItemViewModel creator:
                    creator.OnSessionChanged();
                    break;
                // The mod on screen belongs to the previous session.
                case ItemEditorViewModel:
                    Show(ItemListViewModel, ShellTab.MyMods);
                    break;
            }

            // Not awaited: the command keeps the flyout disabled while it runs.
            if (initResult == SteamInitResult.Success)
                _ = ItemListViewModel.LoadItemsAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Session switch failed", ex);
            _notificationService.ShowError($"{Loc["SessionSwitchFailed"]}: {ex.Message}");
        }
        finally
        {
            IsSwitchingSession = false;
        }
    }

    /// <summary>Moves off a session about to be deleted: to another one, or to a sessionless shell.</summary>
    private async Task LeaveSessionAsync(WorkshopSession session)
    {
        var fallback = SessionList.Sessions.FirstOrDefault(s => s.Id != session.Id);
        if (fallback is not null)
            await SwitchSessionAsync(fallback);
        else
            await ClearActiveSessionAsync();
    }

    /// <summary>Stops the worker and lands on an empty home with the "Add session" call to action.</summary>
    private async Task ClearActiveSessionAsync()
    {
        await _sessionHost.StopAsync();

        _context.Clear();
        _settingsService.Settings.ActiveSessionId = null;
        _settingsService.Save();

        ResetSessionState();
        Connection.SetDisconnected();
        NotifySessionChanged();

        if (CurrentView is ItemEditorViewModel)
            Show(HomeViewModel, ShellTab.Home);
    }

    /// <summary>Drops state tied to the previous session so the UI re-derives it.</summary>
    private void ResetSessionState()
    {
        ItemListViewModel.DisposeAndClearItems();
        HomeViewModel.HeaderImage = null;
        SessionList.ActiveIcon = null;
        SelectedItem = null;
    }

    private void NotifySessionChanged()
    {
        OnPropertyChanged(nameof(ActiveGameName));
        OnPropertyChanged(nameof(ActiveGameInitial));
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(WindowTitle));
        HomeViewModel.OnSessionChanged();
        RefreshPresence();
    }

    [RelayCommand]
    private void AddSession() => OpenAddSessionWizard?.Invoke();

    public AddSessionViewModel CreateAddSessionViewModel() => _viewModels.Create<AddSessionViewModel>();

    /// <summary>Routes a freshly created session through the regular switch path (items, hero, KPIs).</summary>
    public async Task OnSessionAddedAsync()
    {
        try
        {
            var newActive = await _sessionRepository.GetActiveSessionAsync();
            if (newActive is null) return;

            await SessionList.LoadAsync();

            if (SessionList.Find(newActive.Id) is { } sessionInPill)
                await SwitchSessionCommand.ExecuteAsync(sessionInPill);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to activate the new session", ex);
        }
    }

    #endregion

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await Updates.CheckAsync();
            await Updates.ShowWhatsNewIfNeededAsync();
        }
        catch (Exception ex)
        {
            Log.Debug($"Update check failed: {ex.Message}");
        }
    }
}
