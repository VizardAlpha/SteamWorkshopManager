using System;

namespace SteamWorkshopManager.Services.Notifications;

public class NotificationService : INotificationService
{
    public event Action<NotificationState>? StateChanged;

    public void ShowSuccess(string message)
    {
        StateChanged?.Invoke(new NotificationState(true, message, 100, NotificationType.Success));
    }

    public void ShowError(string message)
    {
        StateChanged?.Invoke(new NotificationState(true, message, 0, NotificationType.Error));
    }

    public void Hide()
    {
        StateChanged?.Invoke(new NotificationState(false, string.Empty, 0, NotificationType.Progress));
    }
}
