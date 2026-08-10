using System;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Notifications;

public class NotificationService : INotificationService
{
    private static readonly Logger Log = LogService.GetLogger<NotificationService>();

    public event Action<NotificationState>? StateChanged;

    public void ShowSuccess(string message)
    {
        Log.Info($"Notice: {message}");
        StateChanged?.Invoke(new NotificationState(true, message, 100, NotificationType.Success));
    }

    public void ShowError(string message)
    {
        // Mirror what the user reads on screen, so a bug report and the log
        // carry the same wording instead of a generic failure line.
        Log.Error($"Notice: {message}");
        StateChanged?.Invoke(new NotificationState(true, message, 0, NotificationType.Error));
    }

    public void Hide()
    {
        StateChanged?.Invoke(new NotificationState(false, string.Empty, 0, NotificationType.Progress));
    }
}