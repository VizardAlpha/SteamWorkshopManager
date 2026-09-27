using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Workshop;

namespace SteamWorkshopManager.ViewModels.Editor;

/// <summary>Past versions of an item: changelog list, archive download, Steam QR sign-in they require.</summary>
public partial class ChangelogHistoryViewModel(
    ChangelogScraperService changelogScraper,
    WorkshopDownloadService downloader,
    INotificationService notifications,
    ISessionContext context) : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<ChangelogHistoryViewModel>();

    private WorkshopItem? _item;
    private CancellationTokenSource? _authCts;
    private CancellationTokenSource? _downloadCts;

    public ObservableCollection<ChangeLogEntry> ChangeLogHistory { get; } = [];

    public bool IsLoaded { get; private set; }

    [ObservableProperty]
    private bool _isLoadingChangelogs;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _needsSteamAuth;

    [ObservableProperty]
    private bool _isAuthenticating;

    [ObservableProperty]
    private Bitmap? _qrCodeImage;

    /// <summary>Steam rotates the challenge every few seconds: release each old QR bitmap.</summary>
    partial void OnQrCodeImageChanging(Bitmap? value) => _qrCodeImage?.Dispose();

    public void AttachTo(WorkshopItem item) => _item = item;

    [RelayCommand]
    private async Task LoadChangelogHistoryAsync()
    {
        if (_item is not { } item) return;

        IsLoadingChangelogs = true;
        try
        {
            // A stored refresh token keeps the "downloaded" badges accurate across restarts without a new QR scan.
            if (!SteamAuthService.IsAuthenticated && SteamAuthService.HasRefreshToken)
                await SteamAuthService.TryRefreshAccessTokenAsync();

            // Shown alongside the entries so already-downloaded versions stay visible.
            NeedsSteamAuth = !SteamAuthService.IsAuthenticated;

            var entries = await changelogScraper.GetChangeLogsAsync((ulong)item.PublishedFileId);
            ChangeLogHistory.Clear();
            foreach (var entry in entries)
            {
                entry.IsDownloaded = downloader.IsVersionDownloaded(context.AppId, item.Title, entry.Timestamp);
                ChangeLogHistory.Add(entry);
            }
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            notifications.ShowError($"{Loc["DownloadFailed"]}: {ex.Message}");
        }
        finally
        {
            IsLoadingChangelogs = false;
        }
    }

    [RelayCommand]
    private async Task DownloadVersionAsync(ChangeLogEntry entry)
    {
        if (_item is not { } item) return;

        IsDownloading = true;
        DownloadProgress = 0;
        _downloadCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            var path = await downloader.DownloadVersionAsync(
                context.AppId, (ulong)item.PublishedFileId, item.Title, entry, progress, _downloadCts.Token);

            if (path != null)
            {
                entry.IsDownloaded = true;
                notifications.ShowSuccess(Loc["DownloadComplete"]);
            }
            else if (!_downloadCts.IsCancellationRequested)
            {
                notifications.ShowError(Loc["DownloadFailed"]);
            }
        }
        catch (Exception ex)
        {
            notifications.ShowError($"{Loc["DownloadFailed"]}: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
            _downloadCts.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void OpenVersionFolder(ChangeLogEntry entry)
    {
        if (_item is { } item) downloader.OpenVersionFolder(context.AppId, item.Title, entry.Timestamp);
    }

    [RelayCommand]
    private void OpenChangelogInBrowser()
    {
        if (_item is not { } item) return;
        var url = $"https://steamcommunity.com/sharedfiles/filedetails/changelog/{(ulong)item.PublishedFileId}";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    [RelayCommand]
    private async Task AuthenticateWithSteamAsync()
    {
        IsAuthenticating = true;
        _authCts = new CancellationTokenSource();

        void OnQrUrlChanged(string url)
        {
            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
                var png = new PngByteQRCode(data).GetGraphic(5, [255, 255, 255], [30, 35, 40]);
                using var stream = new MemoryStream(png);
                QrCodeImage = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Log.Debug($"QR code generation failed: {ex.Message}");
            }
        }

        try
        {
            SteamAuthService.QrChallengeUrlChanged += OnQrUrlChanged;
            await SteamAuthService.BeginQrAuthAsync(_authCts.Token);

            notifications.ShowSuccess(Loc["SteamAuthSuccess"]);
            NeedsSteamAuth = false;
            await LoadChangelogHistoryAsync();
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user or by leaving the editor.
        }
        catch (Exception ex)
        {
            notifications.ShowError(ex.Message);
        }
        finally
        {
            SteamAuthService.QrChallengeUrlChanged -= OnQrUrlChanged;
            IsAuthenticating = false;
            QrCodeImage = null;
            _authCts?.Dispose();
            _authCts = null;
        }
    }

    [RelayCommand]
    private void CancelAuth() => _authCts?.Cancel();

    /// <summary>Stops a pending QR sign-in or download when the editor is closed.</summary>
    public void Dispose()
    {
        _authCts?.Cancel();
        _downloadCts?.Cancel();
    }
}
