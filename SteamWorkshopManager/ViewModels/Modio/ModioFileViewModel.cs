using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Platforms.Modio;
using SteamWorkshopManager.Services.UI;

namespace SteamWorkshopManager.ViewModels.Modio;

/// <summary>One published file in the history, with its inline edit and delete states.</summary>
public partial class ModioFileEntry(ModioModfile file) : ObservableObject
{
    public ModioModfile File { get; private set; } = file;

    public long Id => File.Id;

    public string VersionLabel => string.IsNullOrWhiteSpace(File.Version) ? File.Filename ?? $"#{File.Id}" : File.Version;

    public string Changelog => File.Changelog ?? string.Empty;

    public bool HasChangelog => !string.IsNullOrWhiteSpace(File.Changelog);

    public string Details =>
        $"{Formatters.Bytes(File.Filesize)} · {DateTimeOffset.FromUnixTimeSeconds(File.DateAdded).LocalDateTime:g}";

    /// <summary>Flagged by mod.io's virus scan.</summary>
    public bool IsFlagged => File.VirusPositive == 1;

    /// <summary>Not scanned yet or scanning: players may not get it until the scan ends.</summary>
    public bool IsScanning => File.VirusStatus is 0 or 2;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editVersion = string.Empty;

    [ObservableProperty]
    private string _editChangelog = string.Empty;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    private bool _isBusy;

    public void Replace(ModioModfile file)
    {
        File = file;
        OnPropertyChanged(nameof(VersionLabel));
        OnPropertyChanged(nameof(Changelog));
        OnPropertyChanged(nameof(HasChangelog));
        OnPropertyChanged(nameof(Details));
    }
}

/// <summary>
/// File section of a mod.io mod: the folder to zip as a new live version and, once the mod
/// exists, the history of published files (download, make live, edit notes, delete).
/// </summary>
public partial class ModioFileViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<ModioFileViewModel>();

    private readonly IFileDialogService _fileDialog;
    private readonly ModioPublisher _publisher;
    private readonly INotificationService _notifications;
    private readonly CancellationTokenSource _cts = new();

    private ulong? _modId;
    private string _modName = string.Empty;
    private string? _revision;

    // Folder scan runs off the UI thread; the getter reads the last result.
    private long _folderBytes;
    private int _scanVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContentFolder), nameof(ContentFolderSize))]
    private string? _contentFolderPath;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _changelog = string.Empty;

    /// <summary>Live file on mod.io, null when the mod has none yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentFile), nameof(CurrentFileText))]
    private ModioModfile? _currentFile;

    [ObservableProperty]
    private bool _isLoadingHistory;

    [ObservableProperty]
    private string? _historyError;

    public ObservableCollection<ModioFileEntry> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    /// <summary>History entry of the live file, for the actions on the top card.</summary>
    public ModioFileEntry? LiveEntry => History.FirstOrDefault(e => e.IsLive);

    public bool HasLiveEntry => LiveEntry is not null;

    /// <summary>The history needs an existing mod: hidden in the create flow.</summary>
    public bool ShowHistory => _modId is not null;

    public bool HasContentFolder => !string.IsNullOrEmpty(ContentFolderPath);

    public string ContentFolderSize => _folderBytes > 0 ? Formatters.Bytes(_folderBytes) : string.Empty;

    public bool HasCurrentFile => CurrentFile is not null;

    public string CurrentFileText => CurrentFile is { } file
        ? $"{(string.IsNullOrEmpty(file.Version) ? file.Filename : file.Version)} · {Formatters.Bytes(file.Filesize)} · {Formatters.TimeAgo(DateTimeOffset.FromUnixTimeSeconds(file.DateAdded).UtcDateTime)}"
        : string.Empty;

    /// <summary>Another published file became the live one.</summary>
    public event Action? LiveFileChanged;

    public ModioFileViewModel(IFileDialogService fileDialog, ModioPublisher publisher, INotificationService notifications)
    {
        _fileDialog = fileDialog;
        _publisher = publisher;
        _notifications = notifications;
        History.CollectionChanged += (_, _) => OnHistoryChanged();
    }

    /// <summary>Binds the history to an existing mod.</summary>
    public void AttachTo(ulong modId, string modName, string revision)
    {
        _modId = modId;
        _modName = modName;
        _revision = revision;
        OnPropertyChanged(nameof(ShowHistory));
    }

    partial void OnContentFolderPathChanged(string? value) => _ = RefreshFolderSizeAsync(value);

    partial void OnCurrentFileChanged(ModioModfile? value)
    {
        foreach (var entry in History) entry.IsLive = entry.Id == value?.Id;
        OnHistoryChanged();
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(LiveEntry));
        OnPropertyChanged(nameof(HasLiveEntry));
    }

    private async Task RefreshFolderSizeAsync(string? path)
    {
        var version = ++_scanVersion;
        _folderBytes = 0;
        var size = await Task.Run(() => ModFileInfoBuilder.InspectFolder(path).Size);
        if (version != _scanVersion) return;

        _folderBytes = size;
        OnPropertyChanged(nameof(ContentFolderSize));
    }

    /// <summary>The upload to send, null when no folder was picked.</summary>
    public ModioFileUpload? ToUpload() => HasContentFolder
        ? new ModioFileUpload(ContentFolderPath!, NullIfBlank(Version), NullIfBlank(Changelog))
        : null;

    /// <summary>After an upload: the folder is on mod.io now.</summary>
    public void Clear()
    {
        ContentFolderPath = null;
        Version = string.Empty;
        Changelog = string.Empty;
    }

    [RelayCommand]
    private async Task BrowseContentFolderAsync()
    {
        var path = await _fileDialog.OpenFolderAsync(Loc["ContentFolder"]);
        if (!string.IsNullOrEmpty(path)) ContentFolderPath = path;
    }

    [RelayCommand]
    private void FolderDropped(DropPayload payload)
    {
        if (payload.FirstPath is { } path && Directory.Exists(path)) ContentFolderPath = path;
    }

    [RelayCommand]
    private void ClearContentFolder() => ContentFolderPath = null;

    // ─── History ─────────────────────────────────────────────────────────────

    /// <summary>Loads the history, from the cache when the mod hasn't changed since.</summary>
    public Task LoadHistoryAsync() => LoadHistoryCoreAsync(_revision);

    [RelayCommand]
    private Task RefreshHistoryAsync() => LoadHistoryCoreAsync(revision: null);

    private async Task LoadHistoryCoreAsync(string? revision)
    {
        if (_modId is not { } modId) return;

        IsLoadingHistory = true;
        HistoryError = null;
        try
        {
            var files = await _publisher.GetFilesAsync(modId, revision);
            History.Clear();
            foreach (var file in files)
            {
                History.Add(new ModioFileEntry(file)
                {
                    IsLive = file.Id == CurrentFile?.Id,
                    IsDownloaded = _publisher.IsFileDownloaded(_modName, file.Id),
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"mod.io file history failed: {ex.Message}");
            HistoryError = ex is ModioApiException api ? ModioErrors.Describe(api, "ModioHistoryFailed") : Loc["ModioHistoryFailed"];
        }
        finally
        {
            IsLoadingHistory = false;
        }
    }

    [RelayCommand]
    private async Task DownloadFileAsync(ModioFileEntry entry)
    {
        if (_modId is not { } modId || entry.IsDownloading) return;

        entry.IsDownloading = true;
        entry.DownloadProgress = 0;
        try
        {
            var progress = new Progress<double>(p => entry.DownloadProgress = p);
            await _publisher.DownloadFileAsync(modId, entry.Id, _modName, progress, _cts.Token);
            entry.IsDownloaded = true;
            _notifications.ShowSuccess(Loc["DownloadComplete"]);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log.Warning($"mod.io file {entry.Id} download failed: {ex.Message}");
            _notifications.ShowError($"{Loc["DownloadFailed"]}: {ex.Message}");
        }
        finally
        {
            entry.IsDownloading = false;
        }
    }

    [RelayCommand]
    private void OpenFileFolder(ModioFileEntry entry)
    {
        var folder = _publisher.FileFolder(_modName, entry.Id);
        if (!Directory.Exists(folder)) return;
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Debug($"Failed to open {folder}: {ex.Message}"); }
    }

    [RelayCommand]
    private Task MakeLiveAsync(ModioFileEntry entry) => RunOnEntryAsync(entry, async modId =>
    {
        CurrentFile = await _publisher.SetLiveFileAsync(modId, entry.Id);
        _notifications.ShowSuccess(string.Format(Loc["ModioFileNowLive"], entry.VersionLabel));
        LiveFileChanged?.Invoke();
    });

    [RelayCommand]
    private void StartEdit(ModioFileEntry entry)
    {
        entry.EditVersion = entry.File.Version ?? string.Empty;
        entry.EditChangelog = entry.Changelog;
        entry.IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit(ModioFileEntry entry) => entry.IsEditing = false;

    [RelayCommand]
    private Task SaveEditAsync(ModioFileEntry entry) => RunOnEntryAsync(entry, async modId =>
    {
        entry.Replace(await _publisher.EditFileAsync(modId, entry.Id, entry.EditVersion.Trim(), entry.EditChangelog.Trim()));
        entry.IsEditing = false;
    });

    [RelayCommand]
    private void RequestDelete(ModioFileEntry entry) => entry.IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete(ModioFileEntry entry) => entry.IsConfirmingDelete = false;

    [RelayCommand]
    private Task ConfirmDeleteAsync(ModioFileEntry entry) => RunOnEntryAsync(entry, async modId =>
    {
        await _publisher.DeleteFileAsync(modId, entry.Id);
        History.Remove(entry);
    });

    private async Task RunOnEntryAsync(ModioFileEntry entry, Func<ulong, Task> action)
    {
        if (_modId is not { } modId || entry.IsBusy) return;

        entry.IsBusy = true;
        HistoryError = null;
        try
        {
            await action(modId);
        }
        catch (ModioApiException ex)
        {
            HistoryError = ModioErrors.Describe(ex, "ModioFileActionFailed");
        }
        catch (Exception ex)
        {
            Log.Warning($"mod.io file {entry.Id} action failed: {ex.Message}");
            HistoryError = $"{Loc["ModioFileActionFailed"]}: {ex.Message}";
        }
        finally
        {
            entry.IsBusy = false;
            entry.IsConfirmingDelete = false;
        }
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
