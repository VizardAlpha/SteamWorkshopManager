using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkshopManager.Services.UI;
using WorkshopManager.Helpers;
using WorkshopManager.Models;
using WorkshopManager.Services.Cache;
using WorkshopManager.Services.Core;
using WorkshopManager.Services.Log;
using WorkshopManager.Services.Notifications;
using WorkshopManager.Services.Platforms.Modio;
using WorkshopManager.Services.Presence;
using WorkshopManager.Services.Session;
using WorkshopManager.Services.Steam;
using WorkshopManager.Services.Telemetry;

namespace WorkshopManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<SettingsViewModel>();

    private const string GitHubRepoUrl = "https://github.com/VizardAlpha/WorkshopManager";
    private const string DiscordUrl = "https://discord.gg/DCnfnMpbhf";
    private const string PrivacyPolicyUrl = "https://swm-stats.com/Privacy";
    private const string StatsSiteUrl = "https://swm-stats.com";
    private const string ThirdPartyNoticesUrl = $"{GitHubRepoUrl}/blob/master/THIRD-PARTY-NOTICES.md";
    private const string SteamAuthorizedDevicesUrl = "https://store.steampowered.com/account/authorizeddevices";

    private readonly ISettingsService _settingsService;
    private readonly ILogService _logService;
    private readonly ITelemetryService _telemetry;
    private readonly INotificationService _notificationService;
    private readonly IDiscordPresenceService _presence;
    private readonly SessionHost _sessionHost;
    private readonly ICacheStore _cache;

    private const int DefaultCacheLimitMb = 500;
    private const int MinCacheLimitMb = 50;
    private const int MaxCacheLimitMb = 100_000;

    /// <summary>
    /// Which category is currently shown in the right-hand content pane.
    /// Switching this toggles the <c>IsXxxActive</c> flags below, which the
    /// view uses to drive nav highlighting and conditional section visibility.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralActive))]
    [NotifyPropertyChangedFor(nameof(IsCustomizationActive))]
    [NotifyPropertyChangedFor(nameof(IsPrivacyActive))]
    [NotifyPropertyChangedFor(nameof(IsSteamActive))]
    [NotifyPropertyChangedFor(nameof(IsStorageActive))]
    [NotifyPropertyChangedFor(nameof(IsDebugActive))]
    [NotifyPropertyChangedFor(nameof(IsUpdatesActive))]
    [NotifyPropertyChangedFor(nameof(IsAboutActive))]
    private SettingsCategory _activeCategory = SettingsCategory.General;

    public bool IsGeneralActive => ActiveCategory == SettingsCategory.General;
    public bool IsCustomizationActive => ActiveCategory == SettingsCategory.Customization;
    public bool IsPrivacyActive => ActiveCategory == SettingsCategory.Privacy;
    public bool IsSteamActive => ActiveCategory == SettingsCategory.Steam;
    public bool IsStorageActive => ActiveCategory == SettingsCategory.Storage;
    public bool IsDebugActive => ActiveCategory == SettingsCategory.Debug;
    public bool IsUpdatesActive => ActiveCategory == SettingsCategory.Updates;
    public bool IsAboutActive => ActiveCategory == SettingsCategory.About;

    [ObservableProperty]
    private ObservableCollection<LanguageInfo> _availableLanguages = [];

    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    [ObservableProperty]
    private bool _isDebugModeEnabled;

    [ObservableProperty]
    private bool _isTelemetryEnabled;

    [ObservableProperty]
    private bool _includePrereleases;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToastTopLeft))]
    [NotifyPropertyChangedFor(nameof(IsToastTopRight))]
    [NotifyPropertyChangedFor(nameof(IsToastBottomLeft))]
    [NotifyPropertyChangedFor(nameof(IsToastBottomRight))]
    private ToastPosition _toastPosition;

    public bool IsToastTopLeft => ToastPosition == ToastPosition.TopLeft;
    public bool IsToastTopRight => ToastPosition == ToastPosition.TopRight;
    public bool IsToastBottomLeft => ToastPosition == ToastPosition.BottomLeft;
    public bool IsToastBottomRight => ToastPosition == ToastPosition.BottomRight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPresenceOff))]
    [NotifyPropertyChangedFor(nameof(IsPresenceMinimal))]
    [NotifyPropertyChangedFor(nameof(IsPresenceGame))]
    [NotifyPropertyChangedFor(nameof(IsPresenceDetailed))]
    private DiscordPresenceMode _discordPresenceMode;

    public bool IsPresenceOff => DiscordPresenceMode == DiscordPresenceMode.Off;
    public bool IsPresenceMinimal => DiscordPresenceMode == DiscordPresenceMode.Minimal;
    public bool IsPresenceGame => DiscordPresenceMode == DiscordPresenceMode.Game;
    public bool IsPresenceDetailed => DiscordPresenceMode == DiscordPresenceMode.Detailed;

    /// <summary>
    /// On/off switch, owned by the Privacy section: it is the choice that
    /// decides whether anything reaches Discord at all. Customization only
    /// picks how much detail is published.
    /// </summary>
    [ObservableProperty]
    private bool _isDiscordPresenceEnabled;

    /// <summary>
    /// Whether anything actually reaches Discord, as opposed to what the user
    /// asked for. Drives the "not reachable" hint and its retry button: without
    /// it, enabling presence with Discord closed looks like nothing happened.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscordUnreachable))]
    private DiscordConnectionState _discordConnectionState;

    public bool IsDiscordUnreachable => DiscordConnectionState == DiscordConnectionState.Unreachable;

    /// <summary>
    /// Level restored when the switch is turned back on during this session, so
    /// flipping it off and on again does not downgrade someone who had picked
    /// Detailed. Only the current mode is persisted, so a restart while off
    /// loses the level: see the constructor.
    /// </summary>
    private DiscordPresenceMode _lastPresenceLevel;

    partial void OnIsDiscordPresenceEnabledChanged(bool value) =>
        ApplyPresenceMode(value ? _lastPresenceLevel : DiscordPresenceMode.Off);

    /// <summary>
    /// Pseudonymous instance ID surfaced to the user in the Privacy section
    /// so they can quote it in a GDPR access or deletion request. Comes from
    /// <see cref="TelemetryService"/>; the app is initialized at startup so
    /// the instance is already created by the time the settings page opens.
    /// </summary>
    public string InstanceId => _telemetry.InstanceId.ToString();

    [ObservableProperty]
    private bool _isInstanceIdCopied;

    [ObservableProperty]
    private string _logFilePath = string.Empty;

    [ObservableProperty]
    private string _logFolderSizeDisplay = string.Empty;

    /// <summary>
    /// Root folder where app-state lives (bundle, settings, sessions, telemetry
    /// queue, downloads). Logs and the cache live in machine-local folders.
    /// </summary>
    public string DataFolderPath { get; } = AppPaths.Root;

    [ObservableProperty]
    private bool _isCacheEnabled;

    [ObservableProperty]
    private bool _isCacheUnlimited;

    [ObservableProperty]
    private decimal? _cacheLimitMb;

    [ObservableProperty]
    private string _cacheSizeDisplay = string.Empty;

    [ObservableProperty]
    private string _cacheLastUpdateDisplay = string.Empty;

    [ObservableProperty]
    private bool _isCacheFull;

    public string CacheFolderPath => _cache.RootPath;

    public UpdateController Updates { get; }

    /// <summary>Steam account behind the web session (changelog history, downloads), null when signed out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSteamSignedIn))]
    private string? _steamAccountName = SteamAuthService.AccountName;

    public bool IsSteamSignedIn => SteamAccountName is not null;

    private readonly ModioAccount _modioAccount;

    /// <summary>mod.io user behind the stored token, null when signed out.</summary>
    public string? ModioUsername => _modioAccount.IsSignedIn ? _modioAccount.Username ?? string.Empty : null;

    public bool IsModioSignedIn => _modioAccount.IsSignedIn;

    public string AppVersion => AppInfo.Version;

    public SettingsViewModel(
        ISettingsService settingsService,
        ILogService logService,
        ITelemetryService telemetry,
        INotificationService notificationService,
        IDiscordPresenceService presence,
        UpdateController updates,
        SessionHost sessionHost,
        ICacheStore cache,
        ModioAccount modioAccount)
    {
        _modioAccount = modioAccount;
        Updates = updates;
        _sessionHost = sessionHost;
        _cache = cache;
        _settingsService = settingsService;
        _logService = logService;
        _telemetry = telemetry;
        _notificationService = notificationService;
        _presence = presence;

        var languages = LocalizationService.Instance.AvailableLanguages;
        _availableLanguages = new ObservableCollection<LanguageInfo>(languages);
        _selectedLanguage = languages.FirstOrDefault(l => l.Code == _settingsService.Settings.Language);

        _isDebugModeEnabled = _settingsService.Settings.DebugMode;
        _isTelemetryEnabled = _settingsService.Settings.TelemetryEnabled;
        _includePrereleases = _settingsService.Settings.IncludePrereleases;
        _toastPosition = _settingsService.Settings.ToastPosition;
        _discordPresenceMode = _settingsService.Settings.DiscordPresenceMode;
        _isDiscordPresenceEnabled = _discordPresenceMode != DiscordPresenceMode.Off;
        // Settings store the mode, not the level behind an off switch, so
        // starting off leaves nothing to restore: re-enabling lands on the
        // least revealing level.
        _lastPresenceLevel = _isDiscordPresenceEnabled ? _discordPresenceMode : DiscordPresenceMode.Minimal;
        _discordConnectionState = _presence.ConnectionState;
        _presence.ConnectionStateChanged += OnPresenceConnectionChanged;
        _logFilePath = _logService.GetLogFilePath();
        _logFolderSizeDisplay = Formatters.Bytes(_logService.GetLogFolderSize());
        SteamAuthService.AuthStateChanged += OnAuthStateChanged;
        _modioAccount.Changed += OnModioAccountChanged;

        _isCacheEnabled = _settingsService.Settings.CacheEnabled;
        _selectedSteamIdleMode = _settingsService.Settings.SteamIdleMode;
        _steamIdleSeconds = _settingsService.Settings.SteamIdleSeconds;
        _isCacheUnlimited = _settingsService.Settings.CacheLimitMb <= 0;
        _cacheLimitMb = _isCacheUnlimited ? DefaultCacheLimitMb : _settingsService.Settings.CacheLimitMb;
        RefreshCacheInfo();
        _cache.Changed += OnCacheChanged;
        LocalizationService.Instance.LanguageChanged += RefreshCacheInfo;
        LocalizationService.Instance.LanguageChanged += RefreshSteamIdleOptions;

        _ = Updates.CheckAsync();
    }

    /// <summary>Drops the event subscriptions. The shell builds a fresh
    /// instance on every navigation, so without this each visit leaks one.</summary>
    public void Detach()
    {
        _presence.ConnectionStateChanged -= OnPresenceConnectionChanged;
        SteamAuthService.AuthStateChanged -= OnAuthStateChanged;
        _modioAccount.Changed -= OnModioAccountChanged;
        _cache.Changed -= OnCacheChanged;
        LocalizationService.Instance.LanguageChanged -= RefreshCacheInfo;
        LocalizationService.Instance.LanguageChanged -= RefreshSteamIdleOptions;
    }

    // Raised on every cache write, from any thread.
    private void OnCacheChanged() => Dispatcher.UIThread.Post(RefreshCacheInfo);

    private void RefreshCacheInfo()
    {
        CacheSizeDisplay = Formatters.Bytes(_cache.SizeBytes);
        CacheLastUpdateDisplay = _cache.LastWriteUtc is { } last
            ? string.Format(Loc["CacheLastUpdate"], last.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : Loc["CacheNeverUpdated"];
        IsCacheFull = _cache.IsEnabled && _cache.IsFull;
    }

    partial void OnIsCacheEnabledChanged(bool value) => ApplyCacheSettings();

    partial void OnIsCacheUnlimitedChanged(bool value) => ApplyCacheSettings();

    partial void OnCacheLimitMbChanged(decimal? value)
    {
        // Typed decimals are truncated: the setter re-enters with the whole value.
        if (value is { } v && v != decimal.Truncate(v))
        {
            CacheLimitMb = decimal.Truncate(v);
            return;
        }
        ApplyCacheSettings();
    }

    private void ApplyCacheSettings()
    {
        var megabytes = !IsCacheUnlimited
            ? (int)Math.Clamp(CacheLimitMb ?? DefaultCacheLimitMb, MinCacheLimitMb, MaxCacheLimitMb)
            : 0;

        _settingsService.Settings.CacheEnabled = IsCacheEnabled;
        _settingsService.Settings.CacheLimitMb = megabytes;
        _settingsService.Save();
        _cache.Configure(IsCacheEnabled, CacheStore.LimitFromMegabytes(megabytes));
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await _cache.ClearAsync();
        _notificationService.ShowSuccess(Loc["CacheCleared"]);
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        ShellLauncher.Current.OpenFolder(CacheFolderPath);
    }

    /// <summary>Null while the combo box rebuilds its items.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSteamIdleDelayUsed))]
    private SteamIdleMode? _selectedSteamIdleMode;

    [ObservableProperty]
    private decimal? _steamIdleSeconds;

    public bool IsSteamIdleDelayUsed => SelectedSteamIdleMode == SteamIdleMode.AfterInactivity;

    /// <summary>Labelled by <c>SteamIdleModeConverter</c>.</summary>
    public IReadOnlyList<SteamIdleMode> SteamIdleModeOptions => Enum.GetValues<SteamIdleMode>();

    // A new list makes the combo box read the labels again in the new language;
    // it drops its selection meanwhile, so the saved mode is put back.
    private void RefreshSteamIdleOptions()
    {
        OnPropertyChanged(nameof(SteamIdleModeOptions));
        SelectedSteamIdleMode = _settingsService.Settings.SteamIdleMode;
        OnPropertyChanged(nameof(SelectedSteamIdleMode));
    }

    partial void OnSelectedSteamIdleModeChanged(SteamIdleMode? value)
    {
        if (value is not null) ApplySteamIdleSettings();
    }

    partial void OnSteamIdleSecondsChanged(decimal? value) => ApplySteamIdleSettings();

    private void ApplySteamIdleSettings()
    {
        var mode = SelectedSteamIdleMode ?? _settingsService.Settings.SteamIdleMode;
        var seconds = (int)Math.Clamp(SteamIdleSeconds ?? WorkerIdlePolicy.DefaultSeconds,
            WorkerIdlePolicy.MinSeconds, WorkerIdlePolicy.MaxSeconds);

        var settings = _settingsService.Settings;
        if (settings.SteamIdleMode == mode && settings.SteamIdleSeconds == seconds) return;

        settings.SteamIdleMode = mode;
        settings.SteamIdleSeconds = seconds;
        _settingsService.Save();
        _sessionHost.RefreshIdlePolicy();
    }

    private void OnAuthStateChanged() =>
        Dispatcher.UIThread.Post(() => SteamAccountName = SteamAuthService.AccountName);

    [RelayCommand]
    private void SignOutOfSteam()
    {
        SteamAuthService.SignOut();
        _notificationService.ShowSuccess(Loc["SteamSignedOut"]);
    }

    [RelayCommand]
    private void OpenSteamAuthorizedDevices() => OpenUrl(SteamAuthorizedDevicesUrl);

    private void OnModioAccountChanged() => Dispatcher.UIThread.Post(() =>
    {
        OnPropertyChanged(nameof(ModioUsername));
        OnPropertyChanged(nameof(IsModioSignedIn));
    });

    [RelayCommand]
    private void SignOutOfModio()
    {
        _modioAccount.SignOut();
        _notificationService.ShowSuccess(Loc["ModioSignedOut"]);
    }

    [RelayCommand]
    private void OpenModioAccess() => OpenUrl(ModioAccount.AccessPageUrl);

    partial void OnActiveCategoryChanged(SettingsCategory value)
    {
        if (value == SettingsCategory.Updates && Updates.LatestReleaseNotes is null)
            _ = Updates.LoadReleaseNotesAsync();
    }

    // Raised from the library's connection thread.
    private void OnPresenceConnectionChanged() =>
        Dispatcher.UIThread.Post(() => DiscordConnectionState = _presence.ConnectionState);

    [RelayCommand]
    private void RetryDiscordConnection() => _presence.Sync();

    [RelayCommand]
    private void ClearLogs()
    {
        _logService.ClearLogs();
        LogFolderSizeDisplay = Formatters.Bytes(_logService.GetLogFolderSize());
    }

    [RelayCommand]
    private void NavigateToGeneral() => ActiveCategory = SettingsCategory.General;

    [RelayCommand]
    private void NavigateToCustomization() => ActiveCategory = SettingsCategory.Customization;

    [RelayCommand]
    private void NavigateToPrivacy() => ActiveCategory = SettingsCategory.Privacy;

    [RelayCommand]
    private void NavigateToSteam() => ActiveCategory = SettingsCategory.Steam;

    [RelayCommand]
    private void NavigateToStorage() => ActiveCategory = SettingsCategory.Storage;

    [RelayCommand]
    private void NavigateToDebug() => ActiveCategory = SettingsCategory.Debug;

    [RelayCommand]
    private void NavigateToUpdates() => ActiveCategory = SettingsCategory.Updates;

    [RelayCommand]
    private void NavigateToAbout() => ActiveCategory = SettingsCategory.About;

    [RelayCommand]
    private void SelectToastPosition(string position)
    {
        if (!Enum.TryParse<ToastPosition>(position, out var parsed)) return;

        ToastPosition = parsed;
        _settingsService.Settings.ToastPosition = parsed;
        _settingsService.Save();

        // Sample toast so the user sees it jump to the chosen corner.
        _notificationService.ShowSuccess(Loc["ToastPositionPreview"]);
    }

    [RelayCommand]
    private void SelectDiscordPresenceMode(string mode)
    {
        if (!Enum.TryParse<DiscordPresenceMode>(mode, out var parsed)) return;

        ApplyPresenceMode(parsed);
    }

    private void ApplyPresenceMode(DiscordPresenceMode mode)
    {
        DiscordPresenceMode = mode;
        if (mode != DiscordPresenceMode.Off) _lastPresenceLevel = mode;

        _settingsService.Settings.DiscordPresenceMode = mode;
        _settingsService.Save();

        // Connects, drops or just relabels the activity depending on the new mode.
        _presence.Sync();
    }

    partial void OnIsDebugModeEnabledChanged(bool value)
    {
        _settingsService.Settings.DebugMode = value;
        _settingsService.Save();
        _logService.SetDebugMode(value);

        // The toggle changes which file Settings should reveal: debug output
        // when it is on, the regular app log otherwise.
        LogFilePath = _logService.GetLogFilePath();

        // Mirror the toggle into the running worker so it stops emitting at
        // the source instead of relying on the shell to drop entries.
        if (_sessionHost.Worker is { } worker)
            _ = worker.SetDebugModeAsync(value).ContinueWith(
                t => Log.Debug($"Worker debug toggle failed: {t.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    partial void OnIsTelemetryEnabledChanged(bool value)
    {
        _settingsService.Settings.TelemetryEnabled = value;
        _settingsService.Save();
        _ = _telemetry.FlushAsync();
    }

    partial void OnIncludePrereleasesChanged(bool value)
    {
        _settingsService.Settings.IncludePrereleases = value;
        _settingsService.Save();
        _ = Updates.CheckAsync();
        _ = Updates.LoadReleaseNotesAsync();
    }

    partial void OnSelectedLanguageChanged(LanguageInfo? value)
    {
        if (value is null) return;

        _settingsService.Settings.Language = value.Code;
        _settingsService.Save();
        LocalizationService.Instance.CurrentLanguage = value.Code;
    }

    [RelayCommand]
    private async Task CopyInstanceIdAsync()
    {
        var text = InstanceId;
        if (string.IsNullOrWhiteSpace(text) || text == Guid.Empty.ToString()) return;
        await CopyToClipboardAsync(text);

        IsInstanceIdCopied = true;
        try { await Task.Delay(TimeSpan.FromSeconds(2)); }
        finally { IsInstanceIdCopied = false; }
    }

    private static Task CopyToClipboardAsync(string text) => ShellLauncher.Current.CopyTextAsync(text);

    [RelayCommand]
    private void OpenPrivacyPolicy() => OpenUrl(PrivacyPolicyUrl);

    [RelayCommand]
    private void OpenStatsSite() => OpenUrl(StatsSiteUrl);

    [RelayCommand]
    private void OpenGitHub() => OpenUrl(GitHubRepoUrl);

    [RelayCommand]
    private void OpenDiscord() => OpenUrl(DiscordUrl);

    [RelayCommand]
    private void OpenChangelog() => OpenUrl($"{GitHubRepoUrl}/releases");

    [RelayCommand]
    private void OpenThirdPartyNotices() => OpenUrl(ThirdPartyNoticesUrl);

    [RelayCommand]
    private void OpenLogFolder() => ShellLauncher.Current.RevealFile(LogFilePath);

    [RelayCommand]
    private void OpenDataFolder()
    {
        ShellLauncher.Current.OpenFolder(DataFolderPath);
    }

    private static void OpenUrl(string url) => ShellLauncher.Current.OpenUrl(url);
}