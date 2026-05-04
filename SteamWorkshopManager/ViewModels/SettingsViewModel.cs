using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
using Microsoft.Extensions.DependencyInjection;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Telemetry;

namespace SteamWorkshopManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<SettingsViewModel>();

    private const string GitHubRepoUrl = "https://github.com/VizardAlpha/SteamWorkshopManager";
    private const string PrivacyPolicyUrl = "https://swm-stats.com/Privacy";
    private const string StatsSiteUrl = "https://swm-stats.com";

    private readonly ISettingsService _settingsService;
    private readonly ILogService _logService;

    /// <summary>
    /// Which category is currently shown in the right-hand content pane.
    /// Switching this toggles the <c>IsXxxActive</c> flags below, which the
    /// view uses to drive nav highlighting and conditional section visibility.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralActive))]
    [NotifyPropertyChangedFor(nameof(IsPrivacyActive))]
    [NotifyPropertyChangedFor(nameof(IsDebugActive))]
    [NotifyPropertyChangedFor(nameof(IsUpdatesActive))]
    [NotifyPropertyChangedFor(nameof(IsAboutActive))]
    private SettingsCategory _activeCategory = SettingsCategory.General;

    public bool IsGeneralActive => ActiveCategory == SettingsCategory.General;
    public bool IsPrivacyActive => ActiveCategory == SettingsCategory.Privacy;
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

    /// <summary>
    /// Pseudonymous instance ID surfaced to the user in the Privacy section
    /// so they can quote it in a GDPR access or deletion request. Comes from
    /// <see cref="TelemetryService"/>; the app is initialized at startup so
    /// the instance is already created by the time the settings page opens.
    /// </summary>
    public string InstanceId =>
        (TelemetryService.Instance?.InstanceId ?? Guid.Empty).ToString();

    [ObservableProperty]
    private bool _isInstanceIdCopied;

    [ObservableProperty]
    private string _logFilePath = string.Empty;

    /// <summary>
    /// Root folder where app-state lives (bundle, settings, sessions, image
    /// cache, telemetry queue, downloads, tags). Logs live separately under
    /// LocalApplicationData — that's what <see cref="LogFilePath"/> points to.
    /// </summary>
    public string DataFolderPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager");

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private UpdateInfo? _updateInfo;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    public string AppVersion => AppInfo.Version;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _logService = LogService.Instance;

        var languages = LocalizationService.Instance.AvailableLanguages;
        _availableLanguages = new ObservableCollection<LanguageInfo>(languages);
        _selectedLanguage = languages.FirstOrDefault(l => l.Code == _settingsService.Settings.Language);

        _isDebugModeEnabled = _settingsService.Settings.DebugMode;
        _isTelemetryEnabled = _settingsService.Settings.TelemetryEnabled;
        _logFilePath = _logService.GetLogFilePath();

        _ = CheckForUpdatesAsync();
    }

    [RelayCommand]
    private void NavigateToGeneral() => ActiveCategory = SettingsCategory.General;

    [RelayCommand]
    private void NavigateToPrivacy() => ActiveCategory = SettingsCategory.Privacy;

    [RelayCommand]
    private void NavigateToDebug() => ActiveCategory = SettingsCategory.Debug;

    [RelayCommand]
    private void NavigateToUpdates() => ActiveCategory = SettingsCategory.Updates;

    [RelayCommand]
    private void NavigateToAbout() => ActiveCategory = SettingsCategory.About;

    partial void OnIsDebugModeEnabledChanged(bool value)
    {
        _settingsService.Settings.DebugMode = value;
        _settingsService.Save();
        _logService.SetDebugMode(value);

        // Mirror the toggle into the running worker so it stops emitting at
        // the source instead of relying on the shell to drop entries.
        var sessionHost = App.Services.GetService<SessionHost>();
        if (sessionHost?.Worker is { } worker)
            _ = worker.SetDebugModeAsync(value);
    }

    partial void OnIsTelemetryEnabledChanged(bool value)
    {
        _settingsService.Settings.TelemetryEnabled = value;
        _settingsService.Save();
        _ = TelemetryService.Instance?.FlushAsync();
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

    private static async Task CopyToClipboardAsync(string text)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Clipboard is { } clipboard)
            {
                // Avalonia 12 — `SetTextAsync` is an extension method on
                // IClipboard from Avalonia.Input.Platform.ClipboardExtensions,
                // and routes natively on Windows (Win32 clipboard), macOS
                // (NSPasteboard), and Linux (X11 / Wayland).
                await clipboard.SetTextAsync(text);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Clipboard copy failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenPrivacyPolicy() => OpenUrl(PrivacyPolicyUrl);

    [RelayCommand]
    private void OpenStatsSite() => OpenUrl(StatsSiteUrl);

    [RelayCommand]
    private void OpenGitHub() => OpenUrl(GitHubRepoUrl);

    [RelayCommand]
    private void OpenChangelog() => OpenUrl($"{GitHubRepoUrl}/releases");

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingUpdates = true;
        try
        {
            var info = await UpdateCheckerService.CheckForUpdateAsync();
            UpdateInfo = info;
            IsUpdateAvailable = info is not null;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (UpdateInfo?.ReleaseUrl is { } url) OpenUrl(url);
    }

    [RelayCommand]
    private void OpenLogFolder() => RevealInExplorer(LogFilePath);

    [RelayCommand]
    private void OpenDataFolder()
    {
        if (Directory.Exists(DataFolderPath)) RevealFolder(DataFolderPath);
    }

    /// <summary>
    /// Opens the OS file explorer with the given file highlighted. Falls back
    /// to opening the containing folder when the selection syntax isn't
    /// available (Linux).
    /// </summary>
    private static void RevealInExplorer(string filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true,
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"-R \"{filePath}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", folder);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to reveal {filePath}: {ex.Message}");
        }
    }

    private static void RevealFolder(string folder)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true,
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"\"{folder}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", folder);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to open folder {folder}: {ex.Message}");
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to open URL {url}: {ex.Message}");
        }
    }
}