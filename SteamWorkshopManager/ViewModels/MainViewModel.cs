using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Steamworks;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.UI;
using ShellTab = SteamWorkshopManager.Models.ShellTab;

namespace SteamWorkshopManager.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISteamService _steamService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly ISessionRepository _sessionRepository;
    private readonly SessionManager _sessionManager;
    private readonly SessionHost _sessionHost;
    private readonly SessionCleanupService _sessionCleanup;
    private readonly SteamAppMetadataService _appMetadata;
    private string _statusKey = "ConnectingToSteam";

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private bool _isSteamConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private SteamConnectionState _connectionState = SteamConnectionState.Connecting;

    public bool IsConnecting => ConnectionState == SteamConnectionState.Connecting;
    public bool IsConnected => ConnectionState == SteamConnectionState.Connected;
    public bool IsDisconnected => ConnectionState == SteamConnectionState.Disconnected;

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

    /// <summary>
    /// The name of the currently active game/session for display in the shell.
    /// </summary>
    public string ActiveGameName => AppConfig.CurrentSession?.GameName ?? Loc["AppTitle"];

    /// <summary>
    /// Single-character avatar for the session pill (first letter of the game name).
    /// </summary>
    public string ActiveGameInitial =>
        string.IsNullOrEmpty(AppConfig.CurrentSession?.GameName)
            ? "?"
            : AppConfig.CurrentSession.GameName![..1].ToUpperInvariant();

    public bool HasActiveSession => AppConfig.CurrentSession is not null;

    /// <summary>
    /// The window title including the active game name.
    /// </summary>
    public string WindowTitle => $"Steam Workshop Manager - {ActiveGameName}";

    [ObservableProperty]
    private string _statusMessage = "Connecting to Steam...";

    [ObservableProperty]
    private WorkshopItem? _selectedItem;

    [ObservableProperty]
    private bool _isUploadInProgress;

    [ObservableProperty]
    private string _uploadStatusMessage = string.Empty;

    [ObservableProperty]
    private double _uploadProgress;

    [ObservableProperty]
    private string _uploadProgressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccessNotification))]
    [NotifyPropertyChangedFor(nameof(IsErrorNotification))]
    private bool _showNotification;

    [ObservableProperty]
    private string _notificationMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccessNotification))]
    [NotifyPropertyChangedFor(nameof(IsErrorNotification))]
    private NotificationType _notificationType;

    public bool IsSuccessNotification => ShowNotification && NotificationType == NotificationType.Success;
    public bool IsErrorNotification => ShowNotification && NotificationType == NotificationType.Error;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private UpdateInfo? _updateInfo;

    /// <summary>
    /// True while <see cref="SwitchSessionAsync"/> is in flight — drives a
    /// full-window overlay so the user sees "something is happening" instead
    /// of a stale UI during the worker swap + item reload.
    /// </summary>
    [ObservableProperty]
    private bool _isSwitchingSession;

    public ItemListViewModel ItemListViewModel { get; }

    public HomeViewModel HomeViewModel { get; }

    [ObservableProperty]
    private ObservableCollection<WorkshopSession> _sessions = [];

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _activeSessionIcon;

    /// <summary>
    /// Type-to-confirm DELETE state, mirroring <see cref="ItemEditorViewModel"/>.
    /// The flyout picks a session, the modal opens, the user types DELETE,
    /// and the confirm button only enables when the literal passphrase matches.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteSessionConfirmed))]
    private bool _showDeleteSessionConfirmation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteSessionConfirmed))]
    private string _deleteSessionTypedConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteSessionIntroText))]
    private WorkshopSession? _sessionToDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteSessionTotalText))]
    [NotifyPropertyChangedFor(nameof(DeleteSessionDownloadsText))]
    [NotifyPropertyChangedFor(nameof(DeleteSessionDraftsText))]
    private SessionCleanupReport? _sessionDeleteReport;

    public string DeleteSessionIntroText =>
        SessionToDelete is null ? string.Empty : string.Format(Loc["DeleteSessionIntro"], SessionToDelete.GameName);

    public string DeleteSessionTotalText =>
        SessionDeleteReport is null ? string.Empty : string.Format(Loc["DeleteSessionTotal"], SessionDeleteReport.TotalBytesDisplay);

    public string DeleteSessionDownloadsText =>
        SessionDeleteReport is null
            ? string.Empty
            : string.Format(Loc["DeleteSessionItemDownloads"], SessionDeleteReport.DownloadedVersionCount, SessionDeleteReport.DownloadedBytesDisplay);

    public string DeleteSessionDraftsText =>
        SessionDeleteReport is null
            ? string.Empty
            : string.Format(Loc["DeleteSessionItemDrafts"], SessionDeleteReport.DraftCount, SessionDeleteReport.DraftBytesDisplay);

    [ObservableProperty]
    private bool _isDeletingSession;

    public bool IsDeleteSessionConfirmed =>
        ShowDeleteSessionConfirmation
        && string.Equals(DeleteSessionTypedConfirmation, DangerousActions.ConfirmationPassphrase, StringComparison.Ordinal);

    public IProgress<UploadProgress> UploadProgressReporter { get; }

    /// <summary>Parameterless ctor required by Avalonia's design-time tooling.
    /// Pulls everything from the runtime DI container so the production path
    /// matches what's registered in <c>ServiceCollectionExtensions</c>.</summary>
    public MainViewModel() : this(
        App.Services.GetRequiredService<ISteamService>(),
        App.Services.GetRequiredService<IFileDialogService>(),
        App.Services.GetRequiredService<ISettingsService>(),
        App.Services.GetRequiredService<INotificationService>(),
        App.Services.GetRequiredService<ISessionRepository>(),
        App.Services.GetRequiredService<SessionManager>(),
        App.Services.GetRequiredService<SessionHost>(),
        App.Services.GetRequiredService<SessionCleanupService>(),
        App.Services.GetRequiredService<SteamAppMetadataService>())
    { }

    public MainViewModel(
        ISteamService steamService,
        IFileDialogService fileDialogService,
        ISettingsService settingsService,
        INotificationService notificationService,
        ISessionRepository sessionRepository,
        SessionManager sessionManager,
        SessionHost sessionHost,
        SessionCleanupService sessionCleanup,
        SteamAppMetadataService appMetadata)
    {
        _steamService = steamService;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _sessionRepository = sessionRepository;
        _sessionManager = sessionManager;
        _sessionHost = sessionHost;
        _sessionCleanup = sessionCleanup;
        _appMetadata = appMetadata;

        // Worker auto-recovery feedback: refresh UI state on successful respawn,
        // or surface a persistent disconnected banner when we've given up.
        _sessionHost.WorkerRecovered += OnWorkerRecovered;
        _sessionHost.WorkerUnrecoverable += OnWorkerUnrecoverable;

        // Subscribe to notification events
        _notificationService.StateChanged += OnNotificationStateChanged;

        // Initialize localization with saved language
        LocalizationService.Instance.CurrentLanguage = _settingsService.Settings.Language;
        LocalizationService.Instance.Initialize();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        // Initialize Steam auth service with stored tokens
        SteamAuthService.Initialize(_settingsService);

        // Configure progress reporter
        UploadProgressReporter = new Progress<UploadProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsUploadInProgress = p.Percentage < 100;
                UploadStatusMessage = p.Status;
                UploadProgress = p.Percentage;

                if (p.BytesTotal > 0)
                {
                    var processed = FormatBytes(p.BytesProcessed);
                    var total = FormatBytes(p.BytesTotal);
                    UploadProgressText = $"{processed} / {total} ({p.Percentage:F0}%)";
                }
                else
                {
                    UploadProgressText = $"{p.Percentage:F0}%";
                }
            });
        });

        ItemListViewModel = ActivatorUtilities.CreateInstance<ItemListViewModel>(
            App.Services, _notificationService);
        ItemListViewModel.ItemSelected += OnItemSelected;
        ItemListViewModel.CreateRequested += OnCreateRequested;

        HomeViewModel = new HomeViewModel(ItemListViewModel);

        CurrentView = HomeViewModel;
        ActiveTab = ShellTab.Home;

        InitializeSteamAsync();
        _ = CheckForUpdatesAsync();
        _ = LoadSessionsAsync();
    }

    public async Task LoadSessionsAsync()
    {
        try
        {
            var sessions = await _sessionRepository.GetAllSessionsAsync();
            var activeId = AppConfig.CurrentSession?.Id;
            foreach (var session in sessions)
                session.IsActive = session.Id == activeId;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Sessions = new ObservableCollection<WorkshopSession>(sessions);
            });

            // Kick off icon loads right away so the pill/flyout paint quickly
            // with whatever is already cached on disk (or the header fallback).
            foreach (var session in sessions)
            {
                _ = LoadSessionIconAsync(session);
            }

            // Sessions missing a resolved GameIconUrl go through PICS in a
            // single batched round-trip, then persist + refresh the bitmap.
            _ = EnsureIconUrlsAsync(sessions);
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to load sessions in MainViewModel: {ex.Message}");
        }
    }

    private async Task LoadSessionIconAsync(WorkshopSession session)
    {
        try
        {
            // Cache-only lookup: no URL needed when the icon is already on disk.
            var bitmap = await Helpers.SteamImageCache.GetIconAsync(session.AppId, iconUrl: null);

            // Fallback to the wide header while PICS is still resolving the icon hash.
            bitmap ??= await Helpers.SteamImageCache.GetHeaderAsync(session.AppId);
            if (bitmap is null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                session.IconBitmap = bitmap;
                if (session.IsActive)
                    ActiveSessionIcon = bitmap;
            });
        }
        catch (Exception ex)
        {
            Log.Debug($"LoadSessionIconAsync failed for AppId {session.AppId}: {ex.Message}");
        }
    }

    private async Task EnsureIconUrlsAsync(System.Collections.Generic.List<WorkshopSession> sessions)
    {
        // Only sessions whose icon isn't on disk yet need a PICS round-trip.
        var missing = sessions
            .Where(s => s.AppId > 0 && !System.IO.File.Exists(Helpers.SteamImageCache.IconCacheFilePath(s.AppId)))
            .ToList();
        if (missing.Count == 0) return;

        try
        {
            var urls = await _appMetadata.GetIconUrlsAsync(missing.Select(s => s.AppId));

            foreach (var session in missing)
            {
                if (!urls.TryGetValue(session.AppId, out var url)) continue;

                var bitmap = await Helpers.SteamImageCache.GetIconAsync(session.AppId, url);
                if (bitmap is null) continue;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    session.IconBitmap = bitmap;
                    if (session.IsActive)
                        ActiveSessionIcon = bitmap;
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"EnsureIconUrlsAsync failed: {ex.Message}");
        }
    }

    private static readonly Logger Log = LogService.GetLogger<MainViewModel>();

    private static string FormatBytes(ulong bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }

    private void OnLanguageChanged()
    {
        StatusMessage = Loc[_statusKey];
    }

    private void OnWorkerRecovered()
    {
        // Marshal back to the UI thread: SessionHost fires these from the
        // threadpool after Process.Exited bubbles through the respawn pipeline.
        Dispatcher.UIThread.Post(() =>
        {
            IsSteamConnected = _sessionHost.LastInitResult == SteamInitResult.Success;
            ConnectionState = IsSteamConnected
                ? SteamConnectionState.Connected
                : SteamConnectionState.Disconnected;
            HomeViewModel.ConnectionState = ConnectionState;
            SetStatus(IsSteamConnected ? "ConnectedToSteam" : "SteamNotAvailable");
            _notificationService.ShowSuccess(Loc["WorkerRecovered"]);

            // Rehydrate what the dead worker had served: items list.
            if (IsSteamConnected)
                _ = ItemListViewModel.LoadItemsAsync();
        });
    }

    private void OnWorkerUnrecoverable()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsSteamConnected = false;
            ConnectionState = SteamConnectionState.Disconnected;
            HomeViewModel.ConnectionState = SteamConnectionState.Disconnected;
            SetStatus("SteamNotAvailable");
            _notificationService.ShowError(Loc["WorkerUnrecoverable"]);
        });
    }

    private void OnNotificationStateChanged(NotificationState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ShowNotification = state.IsVisible;
            NotificationMessage = state.Message;
            NotificationType = state.Type;

            // Auto-hide after 3 seconds for success/error notifications
            if (state.IsVisible && state.Type != NotificationType.Progress)
            {
                HideNotificationAfterDelay();
            }
        });
    }

    private async void HideNotificationAfterDelay()
    {
        await Task.Delay(3000);
        _notificationService.Hide();
    }

    private void SetStatus(string key)
    {
        _statusKey = key;
        StatusMessage = Loc[key];
    }

    private async void InitializeSteamAsync()
    {
        SetStatus("ConnectingToSteam");
        ConnectionState = SteamConnectionState.Connecting;
        var result = await Task.Run(() => _steamService.Initialize());

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            IsSteamConnected = result == SteamInitResult.Success;
            ConnectionState = result == SteamInitResult.Success
                ? SteamConnectionState.Connected
                : SteamConnectionState.Disconnected;
            HomeViewModel.ConnectionState = ConnectionState;

            switch (result)
            {
                case SteamInitResult.Success:
                    SetStatus("ConnectedToSteam");
                    await ItemListViewModel.LoadItemsAsync();
                    break;
                case SteamInitResult.GameNotOwned:
                    SetStatus("GameNotOwned");
                    _notificationService.ShowError(Loc["GameNotOwnedMessage"]);
                    break;
                default:
                    SetStatus("SteamNotAvailable");
                    break;
            }
        });
    }

    private void DetachCurrentView()
    {
        switch (CurrentView)
        {
            case ItemEditorViewModel editor:
                editor.ItemUpdated -= OnItemUpdated;
                editor.ItemDeleted -= OnItemDeleted;
                break;
            case CreateItemViewModel creator:
                creator.ItemCreated -= OnItemCreated;
                break;
            case SettingsViewModel:
                // SettingsViewModel no longer raises close/add-session events
                // since those UX affordances moved to the topbar / session pill.
                break;
        }
    }

    private void OnItemSelected(WorkshopItem item)
    {
        DetachCurrentView();
        SelectedItem = item;
        var editor = ActivatorUtilities.CreateInstance<ItemEditorViewModel>(
            App.Services, item, UploadProgressReporter);
        editor.ItemUpdated += OnItemUpdated;
        editor.ItemDeleted += OnItemDeleted;
        CurrentView = editor;
        ActiveTab = ShellTab.MyMods;
    }

    private void OnCreateRequested()
    {
        DetachCurrentView();
        var creator = ActivatorUtilities.CreateInstance<CreateItemViewModel>(
            App.Services, UploadProgressReporter);
        creator.ItemCreated += OnItemCreated;
        CurrentView = creator;
    }

    /// <summary>
    /// Post-save flow: keep the editor mounted so the user can verify the
    /// just-saved state — only refresh the matching list row in background so
    /// the catalog is accurate the next time they navigate to "My mods".
    /// </summary>
    private async void OnItemUpdated(PublishedFileId_t fileId)
    {
        var refreshed = await ItemListViewModel.RefreshItemAsync(fileId);
        if (refreshed is null)
        {
            // Single-item fetch failed; trigger a best-effort full reload so
            // the next list visit isn't stale.
            await ItemListViewModel.LoadItemsAsync();
        }
    }

    private async void OnItemDeleted()
    {
        DetachCurrentView();
        CurrentView = ItemListViewModel;
        SelectedItem = null;
        await ItemListViewModel.LoadItemsAsync();
    }

    /// <summary>
    /// Post-publish flow: fetch only the new item (instead of re-querying the
    /// full catalog), insert it into the list, and route straight to the
    /// editor so the user can verify what just shipped — addresses the
    /// "navigation breaks my concentration" feedback. If Steam can't resolve
    /// the freshly-published id (indexing latency, network), fall back to the
    /// list view + full reload + a notification rather than stranding the user.
    /// </summary>
    private async void OnItemCreated(PublishedFileId_t fileId)
    {
        DetachCurrentView();

        var item = await ItemListViewModel.RefreshItemAsync(fileId);
        if (item is not null)
        {
            SelectedItem = item;
            var editor = ActivatorUtilities.CreateInstance<ItemEditorViewModel>(
                App.Services, item, UploadProgressReporter);
            editor.ItemUpdated += OnItemUpdated;
            editor.ItemDeleted += OnItemDeleted;
            CurrentView = editor;
            ActiveTab = ShellTab.MyMods;
            return;
        }

        // Fallback path
        _notificationService.ShowError(Loc["PostPublishFetchFailed"]);
        CurrentView = ItemListViewModel;
        ActiveTab = ShellTab.MyMods;
        await ItemListViewModel.LoadItemsAsync();
    }

    [RelayCommand]
    private void NavigateToHome()
    {
        DetachCurrentView();
        CurrentView = HomeViewModel;
        ActiveTab = ShellTab.Home;
        SelectedItem = null;
    }

    [RelayCommand]
    private void NavigateToList()
    {
        DetachCurrentView();
        CurrentView = ItemListViewModel;
        ActiveTab = ShellTab.MyMods;
        SelectedItem = null;
    }

    [RelayCommand]
    private void NavigateToCreate()
    {
        OnCreateRequested();
        ActiveTab = ShellTab.Create;
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        DetachCurrentView();
        CurrentView = ActivatorUtilities.CreateInstance<SettingsViewModel>(App.Services);
        ActiveTab = ShellTab.Settings;
    }

    [RelayCommand]
    private async Task SwitchSessionAsync(WorkshopSession? session)
    {
        if (session is null || session.Id == AppConfig.CurrentSession?.Id) return;

        Log.Info($"Switching to session: {session.Name}");
        IsSwitchingSession = true;

        try
        {
            // Drop transient state so the UI re-derives from the new worker.
            ItemListViewModel.DisposeAndClearItems();
            HomeViewModel.HeaderImage = null;
            ActiveSessionIcon = null;
            SelectedItem = null;
            IsSteamConnected = false;
            ConnectionState = SteamConnectionState.Connecting;
            HomeViewModel.ConnectionState = SteamConnectionState.Connecting;
            SetStatus("ConnectingToSteam");

            // Swap the worker + update AppConfig to the new session.
            var initResult = await _sessionManager.SwitchSessionAsync(session);

            // Reconcile the Sessions pill checkmark across all entries.
            foreach (var s in Sessions)
                s.IsActive = s.Id == session.Id;

            // Push the new session's cached icon into the pill immediately.
            var active = Sessions.FirstOrDefault(s => s.Id == session.Id);
            if (active is not null)
            {
                ActiveSessionIcon = active.IconBitmap;
                // Kick off the icon load in case the switch exposed a stale bitmap.
                _ = LoadSessionIconAsync(active);
            }

            // Steam state + status copy follow the worker's init result.
            IsSteamConnected = initResult == SteamInitResult.Success;
            ConnectionState = initResult == SteamInitResult.Success
                ? SteamConnectionState.Connected
                : SteamConnectionState.Disconnected;
            HomeViewModel.ConnectionState = ConnectionState;
            SetStatus(initResult switch
            {
                SteamInitResult.Success => "ConnectedToSteam",
                SteamInitResult.GameNotOwned => "GameNotOwned",
                _ => "SteamNotAvailable",
            });

            // Notify header / pill / window title bindings that read AppConfig.
            OnPropertyChanged(nameof(ActiveGameName));
            OnPropertyChanged(nameof(ActiveGameInitial));
            OnPropertyChanged(nameof(HasActiveSession));
            OnPropertyChanged(nameof(WindowTitle));
            HomeViewModel.OnSessionChanged();

            // Propagate the switch to whichever editor/creator is currently
            // mounted. Create-view re-derives tags/branches/drafts from the new
            // AppId in place; the item editor is closed because the mod it was
            // displaying belongs to the previous session.
            switch (CurrentView)
            {
                case CreateItemViewModel creator:
                    creator.OnSessionChanged();
                    break;
                case ItemEditorViewModel:
                    DetachCurrentView();
                    CurrentView = ItemListViewModel;
                    SelectedItem = null;
                    break;
            }

            // Kick off the published-items reload without awaiting: the
            // RelayCommand disables the flyout as long as this handler runs,
            // and the reload can easily take several seconds (Steam query).
            // The ItemListViewModel's own IsLoading flag drives the spinner.
            if (initResult == SteamInitResult.Success)
            {
                _ = ItemListViewModel.LoadItemsAsync();
            }
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

    [RelayCommand]
    private void AddSession()
    {
        OnAddSessionRequested();
    }

    /// <summary>
    /// Tears the worker down and resets shell state so the user lands on a
    /// sessionless home (empty list, no pill, "Add session" CTA). Used when
    /// the active session is deleted with no other sessions to fall back on.
    /// </summary>
    private async Task ClearActiveSessionAsync()
    {
        await _sessionHost.StopAsync();

        AppConfig.Clear();
        _settingsService.Settings.ActiveSessionId = null;
        _settingsService.Save();

        ItemListViewModel.DisposeAndClearItems();
        HomeViewModel.HeaderImage = null;
        ActiveSessionIcon = null;
        SelectedItem = null;
        IsSteamConnected = false;
        ConnectionState = SteamConnectionState.Disconnected;
        HomeViewModel.ConnectionState = SteamConnectionState.Disconnected;

        OnPropertyChanged(nameof(ActiveGameName));
        OnPropertyChanged(nameof(ActiveGameInitial));
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(WindowTitle));
        HomeViewModel.OnSessionChanged();

        if (CurrentView is ItemEditorViewModel)
        {
            DetachCurrentView();
            CurrentView = HomeViewModel;
            ActiveTab = ShellTab.Home;
        }
    }

    [RelayCommand]
    private void RequestDeleteSession(WorkshopSession? session)
    {
        if (session is null) return;

        SessionToDelete = session;
        SessionDeleteReport = _sessionCleanup.BuildReport(session, Sessions.ToList());
        DeleteSessionTypedConfirmation = string.Empty;
        ShowDeleteSessionConfirmation = true;
    }

    [RelayCommand]
    private void CancelDeleteSession()
    {
        ShowDeleteSessionConfirmation = false;
        DeleteSessionTypedConfirmation = string.Empty;
        SessionToDelete = null;
        SessionDeleteReport = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteSessionAsync()
    {
        if (!IsDeleteSessionConfirmed || SessionToDelete is null) return;

        var session = SessionToDelete;
        var snapshot = Sessions.ToList();
        var wasActive = session.Id == AppConfig.CurrentSession?.Id;

        IsDeletingSession = true;
        try
        {
            // If the doomed session is the live one, swap the worker first so
            // PurgeAsync isn't yanking files out from under an active Steam
            // worker. With no fallback we fall back to a sessionless shell —
            // user gets an empty home with an "Add session" button.
            if (wasActive)
            {
                var fallback = Sessions.FirstOrDefault(s => s.Id != session.Id);
                if (fallback is not null)
                    await SwitchSessionAsync(fallback);
                else
                    await ClearActiveSessionAsync();
            }

            await _sessionCleanup.PurgeAsync(session, snapshot);

            var stale = Sessions.FirstOrDefault(s => s.Id == session.Id);
            if (stale is not null)
                Sessions.Remove(stale);

            _notificationService.ShowSuccess(Loc["DeleteSessionSuccess"]);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to delete session", ex);
            _notificationService.ShowError($"{Loc["DeleteSessionFailed"]}: {ex.Message}");
        }
        finally
        {
            IsDeletingSession = false;
            ShowDeleteSessionConfirmation = false;
            DeleteSessionTypedConfirmation = string.Empty;
            SessionToDelete = null;
            SessionDeleteReport = null;
        }
    }

    /// <summary>
    /// Event raised when user wants to add a new session.
    /// </summary>
    public event Action? OpenAddSessionWizard;

    private void OnAddSessionRequested()
    {
        OpenAddSessionWizard?.Invoke();
    }

    private async Task CheckForUpdatesAsync()
    {
        var info = await UpdateCheckerService.CheckForUpdateAsync();
        if (info is not null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateInfo = info;
                IsUpdateAvailable = true;
            });
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (UpdateInfo?.ReleaseUrl is not { } url) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        IsUpdateAvailable = false;
    }
}
