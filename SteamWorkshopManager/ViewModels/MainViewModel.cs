using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services;
using SteamWorkshopManager.Services.Interfaces;

namespace SteamWorkshopManager.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISteamService _steamService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private string _statusKey = "ConnectingToSteam";

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private bool _isSteamConnected;

    /// <summary>
    /// The name of the currently active game/session for display in the header.
    /// </summary>
    public string ActiveGameName => AppConfig.CurrentSession?.GameName ?? "Workshop Manager";

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

    public ItemListViewModel ItemListViewModel { get; }

    public IProgress<UploadProgress> UploadProgressReporter { get; }

    public MainViewModel() : this(new SteamService(), new FileDialogService(), new SettingsService(), new NotificationService()) { }

    public MainViewModel(ISteamService steamService, IFileDialogService fileDialogService, ISettingsService settingsService, INotificationService notificationService)
    {
        _steamService = steamService;
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;
        _notificationService = notificationService;

        // Subscribe to notification events
        _notificationService.StateChanged += OnNotificationStateChanged;

        // Initialize localization with saved language
        LocalizationService.Instance.CurrentLanguage = _settingsService.Settings.Language;
        LocalizationService.Instance.Initialize();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

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

        ItemListViewModel = new ItemListViewModel(_steamService);
        ItemListViewModel.ItemSelected += OnItemSelected;
        ItemListViewModel.CreateRequested += OnCreateRequested;

        CurrentView = ItemListViewModel;

        InitializeSteamAsync();
        _ = CheckForUpdatesAsync();
    }

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
        var connected = await Task.Run(() => _steamService.Initialize());

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            IsSteamConnected = connected;

            if (connected)
            {
                SetStatus("ConnectedToSteam");
                await ItemListViewModel.LoadItemsAsync();
            }
            else
            {
                SetStatus("SteamNotAvailable");
            }
        });
    }

    private void OnItemSelected(WorkshopItem item)
    {
        SelectedItem = item;
        CurrentView = new ItemEditorViewModel(item, _steamService, _fileDialogService, _settingsService, _notificationService, UploadProgressReporter);
        if (CurrentView is ItemEditorViewModel editor)
        {
            editor.CloseRequested += OnEditorCloseRequested;
            editor.ItemUpdated += OnItemUpdated;
            editor.ItemDeleted += OnItemDeleted;
        }
    }

    private void OnCreateRequested()
    {
        CurrentView = new CreateItemViewModel(_steamService, _fileDialogService, _settingsService, _notificationService, UploadProgressReporter);
        if (CurrentView is CreateItemViewModel creator)
        {
            creator.CloseRequested += OnEditorCloseRequested;
            creator.ItemCreated += OnItemCreated;
        }
    }

    private void OnEditorCloseRequested()
    {
        CurrentView = ItemListViewModel;
        SelectedItem = null;
    }

    private async void OnItemUpdated()
    {
        CurrentView = ItemListViewModel;
        SelectedItem = null;
        await ItemListViewModel.LoadItemsAsync();
    }

    private async void OnItemDeleted()
    {
        CurrentView = ItemListViewModel;
        SelectedItem = null;
        await ItemListViewModel.LoadItemsAsync();
    }

    private async void OnItemCreated()
    {
        CurrentView = ItemListViewModel;
        await ItemListViewModel.LoadItemsAsync();
    }

    [RelayCommand]
    private void NavigateToList()
    {
        CurrentView = ItemListViewModel;
        SelectedItem = null;
    }

    [RelayCommand]
    private void NavigateToCreate()
    {
        OnCreateRequested();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        var settingsVm = new SettingsViewModel(_settingsService);
        settingsVm.CloseRequested += OnSettingsCloseRequested;
        settingsVm.AddSessionRequested += OnAddSessionRequested;
        CurrentView = settingsVm;
    }

    private void OnSettingsCloseRequested()
    {
        CurrentView = ItemListViewModel;
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
