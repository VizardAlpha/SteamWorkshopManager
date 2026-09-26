using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;

namespace SteamWorkshopManager.ViewModels;

public record ReleaseNotesItem(string Version, string DateText, string BbCode, bool IsPrerelease);

/// <summary>
/// Update state shared by the shell banner, the Settings page and the "What's new" dialog:
/// check, in-app download + install, release notes.
/// </summary>
public partial class UpdateController : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<UpdateController>();

    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _installCts;

    public UpdateController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    [NotifyPropertyChangedFor(nameof(CanInstallInApp))]
    private UpdateInfo? _updateInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    private bool _isBannerDismissed;

    public bool IsBannerVisible => IsUpdateAvailable && !IsBannerDismissed;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallInApp))]
    private bool _canSelfUpdate;

    /// <summary>False: the button falls back to opening the release page.</summary>
    public bool CanInstallInApp => CanSelfUpdate && UpdateInfo?.CanInstallInApp == true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string? _installStatus;

    [ObservableProperty]
    private string? _installError;

    /// <summary>Mirrors the shell's upload lock: installing restarts the app.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isUploadInProgress;

    /// <summary>Newest release on the user's channel, shown in Settings.</summary>
    [ObservableProperty]
    private ReleaseNotesItem? _latestReleaseNotes;

    [ObservableProperty]
    private bool _isLoadingReleaseNotes;

    [ObservableProperty]
    private bool _showWhatsNew;

    [ObservableProperty]
    private string _whatsNewVersion = string.Empty;

    [ObservableProperty]
    private string _whatsNewBbCode = string.Empty;

    [RelayCommand]
    public async Task CheckAsync()
    {
        IsChecking = true;
        try
        {
            var info = await UpdateCheckerService.CheckForUpdateAsync(_settingsService.Settings.IncludePrereleases);
            var canSelfUpdate = info is not null && await Task.Run(AppUpdater.CanSelfUpdate);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateInfo = info;
                IsUpdateAvailable = info is not null;
                CanSelfUpdate = canSelfUpdate;
            });
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool CanInstall() => !IsInstalling && !IsUploadInProgress;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (!CanInstallInApp || UpdateInfo is not { } info)
        {
            OpenReleasePage();
            return;
        }

        IsInstalling = true;
        InstallError = null;
        InstallProgress = 0;
        InstallStatus = Loc["UpdateDownloading"];
        _installCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p => InstallProgress = p);
            var payload = await AppUpdater.DownloadAndStageAsync(info, progress, _installCts.Token);

            InstallStatus = Loc["UpdateRestarting"];
            AppUpdater.LaunchInstaller(payload);

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.TryShutdown();
        }
        catch (OperationCanceledException)
        {
            InstallStatus = null;
            IsInstalling = false;
        }
        catch (Exception ex)
        {
            Log.Error("In-app update failed", ex);
            InstallError = $"{Loc["UpdateInstallFailed"]}: {ex.Message}";
            InstallStatus = null;
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void CancelInstall() => _installCts?.Cancel();

    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = UpdateInfo?.ReleaseUrl;
        if (!UpdateCheckerService.IsTrustedReleasePageUrl(url)) url = $"{UpdateCheckerService.RepoUrl}/releases";
        OpenUrl(url!);
    }

    [RelayCommand]
    private void DismissBanner() => IsBannerDismissed = true;

    public async Task LoadReleaseNotesAsync()
    {
        if (IsLoadingReleaseNotes) return;
        IsLoadingReleaseNotes = true;
        try
        {
            var n = await UpdateCheckerService.GetLatestReleaseNotesAsync(_settingsService.Settings.IncludePrereleases);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LatestReleaseNotes = n is null
                    ? null
                    : new ReleaseNotesItem(
                        $"v{n.Version}",
                        n.PublishedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentUICulture) ?? string.Empty,
                        MarkdownToBbCode.Convert(n.Markdown),
                        n.IsPrerelease);
            });
        }
        finally
        {
            IsLoadingReleaseNotes = false;
        }
    }

    /// <summary>Shows the notes of the running version once: first launch and right after an update.</summary>
    public async Task ShowWhatsNewIfNeededAsync()
    {
        var current = AppInfo.Version;
        if (_settingsService.Settings.LastSeenVersion == current) return;

        var notes = await UpdateCheckerService.GetReleaseNotesAsync(current);
        if (notes is null) return; // Offline: try again next launch.

        _settingsService.Settings.LastSeenVersion = current;
        _settingsService.Save();

        if (string.IsNullOrWhiteSpace(notes.Markdown)) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WhatsNewVersion = $"v{current}";
            WhatsNewBbCode = MarkdownToBbCode.Convert(notes.Markdown);
            ShowWhatsNew = true;
        });
    }

    [RelayCommand]
    private void CloseWhatsNew() => ShowWhatsNew = false;

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
