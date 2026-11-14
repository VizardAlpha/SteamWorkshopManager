using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Steamworks;
using WorkshopManager.Core.Steam;
using WorkshopManager.Core.Sessions;
using WorkshopManager.Core.Workshop;
using WorkshopManager.Helpers;
using WorkshopManager.Models;
using WorkshopManager.Services.Core;
using WorkshopManager.Services.Log;
using WorkshopManager.Services.Notifications;
using WorkshopManager.Services.Steam;
using WorkshopManager.Services.Telemetry;
using WorkshopManager.Services.UI;
using WorkshopManager.Services.Workshop;
using WorkshopManager.ViewModels.Shell;

namespace WorkshopManager.ViewModels;

/// <summary>
/// "Update selected" of the mod list: shows what would be sent, takes the change notes,
/// then uploads the mods one by one in the background through the upload toast.
/// </summary>
public partial class BulkUpdateViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<BulkUpdateViewModel>();

    private const int MaxNamesListed = 5;

    private readonly UploadProgressViewModel _upload;
    private readonly UploadCancellation _cancellation;
    private readonly ISteamService _steam;
    private readonly ISettingsService _settings;
    private readonly ITelemetryService _telemetry;
    private readonly ISessionContext _context;
    private readonly IFileDialogService _fileDialog;
    private readonly INotificationService _notifications;

    private IReadOnlyList<WorkshopItem> _selected = [];
    private IReadOnlyList<BulkUpdateEntry> _ready = [];
    private BulkChangelog? _fileChangelog;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isPreparing;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private int _readyCount;

    [ObservableProperty]
    private string _readyText = string.Empty;

    [ObservableProperty]
    private string? _unchangedText;

    [ObservableProperty]
    private string? _noFolderText;

    /// <summary>Notes for every mod, or for the mods the file has no section for.</summary>
    [ObservableProperty]
    private string _changelogText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangelogFile))]
    private string? _changelogFileName;

    [ObservableProperty]
    private string? _changelogFileStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string? _changelogError;

    public bool HasChangelogFile => ChangelogFileName is not null;

    /// <summary>Raised on the UI thread when the dialog starts a batch: the list leaves its selection mode.</summary>
    public event Action? Started;

    /// <summary>Raised on the UI thread once the batch is over: the list reloads.</summary>
    public event Action? Finished;

    public BulkUpdateViewModel(
        UploadProgressViewModel upload,
        UploadCancellation cancellation,
        ISteamService steam,
        ISettingsService settings,
        ITelemetryService telemetry,
        ISessionContext context,
        IFileDialogService fileDialog,
        INotificationService notifications)
    {
        _upload = upload;
        _cancellation = cancellation;
        _steam = steam;
        _settings = settings;
        _telemetry = telemetry;
        _context = context;
        _fileDialog = fileDialog;
        _notifications = notifications;
        _upload.InProgressChanged += _ => StartCommand.NotifyCanExecuteChanged();
    }

    public async Task OpenAsync(IReadOnlyList<WorkshopItem> selected)
    {
        _selected = selected;
        _ready = [];
        _fileChangelog = null;
        ChangelogText = string.Empty;
        ChangelogFileName = null;
        ChangelogFileStatus = null;
        ChangelogError = null;
        ReadyCount = 0;
        ReadyText = string.Empty;
        UnchangedText = null;
        NoFolderText = null;
        Title = string.Format(Loc["BulkUpdateTitle"], selected.Count);
        IsPreparing = true;
        IsOpen = true;

        try
        {
            // Folder sizes are read from disk: off the UI thread.
            var stored = selected.ToDictionary(i => i.Id, i => _settings.GetContentFolderInfo(i.Id));
            var plan = await Task.Run(() => selected.Select(i => BulkUpdatePlanner.Classify(i, stored[i.Id])).ToList());
            if (!ReferenceEquals(_selected, selected)) return;

            _ready = plan.Where(e => e.State == BulkUpdateState.Ready).ToList();
            ReadyCount = _ready.Count;
            ReadyText = _ready.Count == 0
                ? Loc["BulkUpdateNothing"]
                : string.Format(Loc["BulkUpdateReady"], _ready.Count, Formatters.Bytes(_ready.Sum(e => e.SizeBytes)));

            var unchanged = plan.Count(e => e.State == BulkUpdateState.Unchanged);
            UnchangedText = unchanged == 0 ? null : string.Format(Loc["BulkUpdateUnchanged"], unchanged);

            var noFolder = plan.Where(e => e.State is BulkUpdateState.NoFolder or BulkUpdateState.FolderMissing).ToList();
            NoFolderText = noFolder.Count == 0
                ? null
                : string.Format(Loc["BulkUpdateNoFolder"], noFolder.Count, Names(noFolder.Select(e => e.Item.Title)));
        }
        catch (Exception ex)
        {
            Log.Error("Bulk update plan failed", ex);
            ReadyText = Loc["BulkUpdateNothing"];
        }
        finally
        {
            IsPreparing = false;
        }
    }

    [RelayCommand]
    private async Task PickChangelogFileAsync()
    {
        var path = await _fileDialog.OpenFileAsync(Loc["BulkUpdateChangelogFile"], BulkChangelogFile.Extensions);
        if (path is null) return;

        var result = await Task.Run(() => BulkChangelogFile.Read(path));
        ChangelogFileName = Path.GetFileName(path);
        _fileChangelog = result.Changelog;
        RefreshChangelogStatus(result.ErrorKey);
    }

    [RelayCommand]
    private void RemoveChangelogFile()
    {
        _fileChangelog = null;
        ChangelogFileName = null;
        ChangelogFileStatus = null;
        ChangelogError = null;
    }

    partial void OnChangelogTextChanged(string value)
    {
        if (IsOpen) RefreshChangelogStatus(null);
    }

    /// <summary>The notes as they will be sent: the file's sections over the typed text.</summary>
    private BulkChangelog CurrentChangelog()
    {
        if (_fileChangelog is null) return BulkChangelog.ForAll(ChangelogText);
        // The typed text wins over the file's own leading text when both exist.
        return string.IsNullOrWhiteSpace(ChangelogText) ? _fileChangelog : _fileChangelog.WithFallback(ChangelogText);
    }

    private void RefreshChangelogStatus(string? fileErrorKey)
    {
        if (fileErrorKey is not null)
        {
            ChangelogFileStatus = null;
            ChangelogError = Loc[fileErrorKey];
            return;
        }

        var changelog = CurrentChangelog();
        var targets = _ready.Select(e => e.Item).ToList();

        if (_fileChangelog is not null)
        {
            var matched = targets.Count(_fileChangelog.HasSectionFor);
            var status = string.Format(Loc["BulkChangelogFileLoaded"], matched, targets.Count);
            var unknown = _fileChangelog.SectionsWithoutMod(targets);
            if (unknown.Count > 0)
                status += " " + string.Format(Loc["BulkChangelogUnknownSections"], Names(unknown));
            ChangelogFileStatus = status;
        }

        var tooLong = changelog.TooLongFor(targets);
        ChangelogError = tooLong.Count == 0
            ? null
            : string.Format(Loc["BulkChangelogTooLong"], BulkChangelog.MaxNoteLength, Names(tooLong.Select(i => i.Title)));
    }

    private bool CanStart() => ReadyCount > 0 && ChangelogError is null && !_upload.IsInProgress;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        var entries = _ready;
        var changelog = CurrentChangelog();
        IsOpen = false;
        Started?.Invoke();
        _ = RunAsync(entries, changelog);
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    private async Task RunAsync(IReadOnlyList<BulkUpdateEntry> entries, BulkChangelog changelog)
    {
        BulkUpdateSummary? summary = null;
        try
        {
            using var scope = _cancellation.Begin();
            var progress = new DirectProgress<BulkUpdateProgress>(
                p => Dispatcher.UIThread.Post(() => _upload.ApplyBatch(p)));

            summary = await new BulkUpdateRunner().RunAsync(
                entries, (entry, own) => SendAsync(entry, changelog.For(entry.Item), own), progress, scope.Token);
        }
        catch (Exception ex)
        {
            Log.Error("Bulk update failed", ex);
        }

        Dispatcher.UIThread.Post(() =>
        {
            _upload.EndBatch();
            Announce(summary, entries.Count);
            Finished?.Invoke();
        });
    }

    private async Task<UpdateItemOutcome> SendAsync(BulkUpdateEntry entry, string notes, IProgress<UploadProgress> own)
    {
        var outcome = await _steam.UpdateItemAsync(
            entry.Item.PublishedFileId, null, null, entry.Folder, null, null, null, notes, own);
        if (!outcome.Success) return outcome;

        // Same record as a save from the editor: the next bulk update sees this mod as unchanged.
        var info = await Task.Run(() => ModFileInfoBuilder.BuildForFolder(entry.Folder));
        if (info is not null) _settings.SetContentFolderInfo(entry.Item.Id, info);
        _telemetry.Track(TelemetryEventTypes.ModUpdated, _context.AppId);
        return outcome;
    }

    private void Announce(BulkUpdateSummary? summary, int total)
    {
        if (summary is null)
        {
            _notifications.ShowError(Loc["UpdateFailed"]);
            return;
        }

        if (summary.Cancelled)
            _notifications.ShowError(string.Format(Loc["BulkUpdateCancelled"], summary.Updated, total));
        else if (summary.Failed.Count > 0)
            _notifications.ShowError(string.Format(
                Loc["BulkUpdatePartial"], summary.Updated, total, Names(summary.Failed.Select(Describe))));
        else if (summary.LegalAgreementPending)
            _notifications.ShowError(Loc["WorkshopLegalAgreementRequired"]);
        else
            _notifications.ShowSuccess(string.Format(Loc["BulkUpdateDone"], summary.Updated));
    }

    /// <summary>"Title (why)": the reason tells the user whether trying again can help.</summary>
    private string Describe(BulkUpdateFailure failure) => failure.Result switch
    {
        null => failure.Title,
        // On an update, Steam uses this code for these two causes only.
        EResult.k_EResultLimitExceeded => $"{failure.Title} ({Loc["BulkUpdateLimitExceeded"]})",
        var result => $"{failure.Title} ({SteamErrorMapper.GetErrorMessage(result.Value)})",
    };

    private static string Names(IEnumerable<string> names)
    {
        var all = names.ToList();
        var shown = string.Join(", ", all.Take(MaxNamesListed));
        return all.Count > MaxNamesListed ? $"{shown}, +{all.Count - MaxNamesListed}" : shown;
    }
}
