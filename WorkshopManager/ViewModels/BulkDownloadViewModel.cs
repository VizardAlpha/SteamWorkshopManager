using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkshopManager.Core.Sessions;
using WorkshopManager.Core.Workshop;
using WorkshopManager.Helpers;
using WorkshopManager.Models;
using WorkshopManager.Services.Log;
using WorkshopManager.Services.Notifications;
using WorkshopManager.Services.Steam;
using WorkshopManager.Services.UI;
using WorkshopManager.Services.Workshop;
using WorkshopManager.ViewModels.Shell;

namespace WorkshopManager.ViewModels;

/// <summary>
/// "Download selected" of the mod list: backs up the latest version of the selected mods as zips
/// in the app's folder, in the background through the upload toast.
/// </summary>
public partial class BulkDownloadViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<BulkDownloadViewModel>();

    private const int MaxNamesListed = 5;
    // The zip is at most as big as the content; the margin covers the file system's own overhead.
    private const double SpaceMargin = 1.05;

    private readonly UploadProgressViewModel _upload;
    private readonly UploadCancellation _cancellation;
    private readonly ISteamService _steam;
    private readonly ISessionContext _context;
    private readonly INotificationService _notifications;

    private IReadOnlyList<WorkshopItem> _selected = [];
    private IReadOnlyList<WorkshopItem> _toDownload = [];
    private string _folder = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private int _downloadCount;

    [ObservableProperty]
    private string _downloadText = string.Empty;

    [ObservableProperty]
    private string? _skippedText;

    [ObservableProperty]
    private string _folderText = string.Empty;

    /// <summary>Not enough room on the disk: nothing starts until there is.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string? _spaceError;

    /// <summary>Raised on the UI thread when the dialog starts a batch: the list leaves its selection mode.</summary>
    public event Action? Started;

    public BulkDownloadViewModel(
        UploadProgressViewModel upload,
        UploadCancellation cancellation,
        ISteamService steam,
        ISessionContext context,
        INotificationService notifications)
    {
        _upload = upload;
        _cancellation = cancellation;
        _steam = steam;
        _context = context;
        _notifications = notifications;
        _upload.InProgressChanged += _ => StartCommand.NotifyCanExecuteChanged();
    }

    public async Task OpenAsync(IReadOnlyList<WorkshopItem> selected)
    {
        _selected = selected;
        _folder = AppPaths.WorkshopBackups(_context.AppId);
        FolderText = _folder;
        Title = string.Format(Loc["BulkDownloadTitle"], selected.Count);
        SkippedText = null;
        SpaceError = null;
        DownloadCount = 0;
        DownloadText = string.Empty;
        IsOpen = true;

        await RefreshAsync();
    }

    /// <summary>Looks at the disk again: what is already backed up, and how much room is left.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var selected = _selected;
        var folder = _folder;
        try
        {
            var (missing, free) = await Task.Run(() =>
                (selected.Where(i => ModBackupStore.FindCurrent(folder, i) is null).ToList(), ModBackupStore.FreeSpace(folder)));
            if (!ReferenceEquals(_selected, selected)) return;

            _toDownload = missing;
            DownloadCount = missing.Count;
            var size = missing.Sum(i => i.FileSize);
            DownloadText = missing.Count == 0
                ? Loc["BulkDownloadNothing"]
                : string.Format(Loc["BulkDownloadReady"], missing.Count, Formatters.Bytes(size));

            var onDisk = selected.Count - missing.Count;
            SkippedText = onDisk == 0 ? null : string.Format(Loc["BulkDownloadSkipped"], onDisk);

            var needed = (long)(size * SpaceMargin);
            SpaceError = free is { } available && available < needed
                ? string.Format(Loc["BulkDownloadNoSpace"], Formatters.Bytes(needed), Formatters.Bytes(available))
                : null;
        }
        catch (Exception ex)
        {
            Log.Error("Bulk download plan failed", ex);
            DownloadText = Loc["BulkDownloadNothing"];
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        System.IO.Directory.CreateDirectory(_folder);
        ShellLauncher.Current.OpenFolder(_folder);
    }

    private bool CanStart() => DownloadCount > 0 && SpaceError is null && !_upload.IsInProgress;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        var items = _toDownload;
        var folder = _folder;
        IsOpen = false;
        Started?.Invoke();
        _ = RunAsync(items, folder);
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    private async Task RunAsync(IReadOnlyList<WorkshopItem> items, string folder)
    {
        BulkDownloadSummary? summary = null;
        try
        {
            using var scope = _cancellation.Begin();
            var progress = new DirectProgress<BulkUpdateProgress>(
                p => Dispatcher.UIThread.Post(() => _upload.ApplyBatch(p, "BulkDownloadStatus")));

            summary = await new BulkDownloadRunner().RunAsync(
                items, (item, own, ct) => FetchAsync(item, folder, own, ct), progress, scope.Token);
        }
        catch (Exception ex)
        {
            Log.Error("Bulk download failed", ex);
        }

        Dispatcher.UIThread.Post(() =>
        {
            _upload.EndBatch();
            Announce(summary, items.Count);
        });
    }

    private async Task<bool> FetchAsync(WorkshopItem item, string folder, IProgress<UploadProgress> own, CancellationToken ct)
    {
        var outcome = await _steam.DownloadItemAsync(item.PublishedFileId, own);
        if (!outcome.Success || outcome.Path is null) return false;

        try
        {
            await ModBackupStore.StoreAsync(outcome.Path, folder, item, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not store the backup of '{item.Title}': {ex.Message}");
            return false;
        }
    }

    private void Announce(BulkDownloadSummary? summary, int total)
    {
        if (summary is null)
        {
            _notifications.ShowError(Loc["BulkDownloadFailed"]);
            return;
        }

        if (summary.Cancelled)
            _notifications.ShowError(string.Format(Loc["BulkDownloadCancelled"], summary.Downloaded, total));
        else if (summary.Failed.Count > 0)
            _notifications.ShowError(string.Format(Loc["BulkDownloadPartial"], summary.Downloaded, total, Names(summary.Failed)));
        else
            _notifications.ShowSuccess(string.Format(Loc["BulkDownloadDone"], summary.Downloaded));
    }

    private static string Names(IReadOnlyList<string> names)
    {
        var shown = string.Join(", ", names.Take(MaxNamesListed));
        return names.Count > MaxNamesListed ? $"{shown}, +{names.Count - MaxNamesListed}" : shown;
    }
}
