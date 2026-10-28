using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;

namespace SteamWorkshopManager.ViewModels.Shell;

/// <summary>
/// Type-to-confirm session deletion, same pattern as the per-mod delete.
/// <paramref name="leaveSession"/> moves the shell off the session before its files are purged.
/// </summary>
public partial class SessionDeletionViewModel(
    SessionListViewModel sessionList,
    Func<WorkshopSession, Task> leaveSession,
    SessionCleanupService cleanup,
    INotificationService notifications,
    ISessionContext context) : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<SessionDeletionViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmed))]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmed))]
    private string _typedConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntroText))]
    private WorkshopSession? _session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalText))]
    [NotifyPropertyChangedFor(nameof(DownloadsText))]
    [NotifyPropertyChangedFor(nameof(DraftsText))]
    private SessionCleanupReport? _report;

    [ObservableProperty]
    private bool _isDeleting;

    public bool IsConfirmed =>
        IsOpen && string.Equals(TypedConfirmation, DangerousActions.ConfirmationPassphrase, StringComparison.Ordinal);

    public string IntroText =>
        Session is null ? string.Empty : string.Format(Loc["DeleteSessionIntro"], Session.GameName);

    public string TotalText =>
        Report is null ? string.Empty : string.Format(Loc["DeleteSessionTotal"], Report.TotalBytesDisplay);

    public string DownloadsText =>
        Report is null
            ? string.Empty
            : string.Format(Loc["DeleteSessionItemDownloads"], Report.DownloadedVersionCount, Report.DownloadedBytesDisplay);

    public string DraftsText =>
        Report is null
            ? string.Empty
            : string.Format(Loc["DeleteSessionItemDrafts"], Report.DraftCount, Report.DraftBytesDisplay);

    [RelayCommand]
    private void Request(WorkshopSession? session)
    {
        if (session is null) return;

        Session = session;
        Report = cleanup.BuildReport(session, sessionList.Sessions.ToList());
        TypedConfirmation = string.Empty;
        IsOpen = true;
    }

    [RelayCommand]
    private void Cancel() => Close();

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (!IsConfirmed || Session is null) return;

        var session = Session;
        var snapshot = sessionList.Sessions.ToList();

        IsDeleting = true;
        try
        {
            // Never purge files from under a live Steam worker.
            if (session.Id == context.Current?.Id)
                await leaveSession(session);

            await cleanup.PurgeAsync(session, snapshot);
            sessionList.Remove(session.Id);

            notifications.ShowSuccess(Loc["DeleteSessionSuccess"]);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to delete session", ex);
            notifications.ShowError($"{Loc["DeleteSessionFailed"]}: {ex.Message}");
        }
        finally
        {
            IsDeleting = false;
            Close();
        }
    }

    private void Close()
    {
        IsOpen = false;
        TypedConfirmation = string.Empty;
        Session = null;
        Report = null;
    }
}
