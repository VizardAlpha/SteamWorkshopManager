using System;

namespace SteamWorkshopManager.Services.Interfaces;

public interface INotificationService
{
    void ShowProgress(string message, double progress);
    void ShowSuccess(string message);
    void ShowError(string message);
    void Hide();

    event Action<NotificationState>? StateChanged;
}

public record NotificationState(
    bool IsVisible,
    string Message,
    double Progress,
    NotificationType Type
);

public enum NotificationType
{
    Progress,
    Success,
    Error
}
