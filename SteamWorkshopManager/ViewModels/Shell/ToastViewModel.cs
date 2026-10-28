using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Notifications;

namespace SteamWorkshopManager.ViewModels.Shell;

/// <summary>Success/error toasts raised through <see cref="INotificationService"/>, and the corner they dock to.</summary>
public partial class ToastViewModel : ViewModelBase
{
    private const int AutoHideDelayMs = 3000;

    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private int _generation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    private bool _isVisible;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    private NotificationType _type;

    [ObservableProperty]
    private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Right;

    [ObservableProperty]
    private VerticalAlignment _verticalAlignment = VerticalAlignment.Bottom;

    public bool IsSuccess => IsVisible && Type == NotificationType.Success;
    public bool IsError => IsVisible && Type == NotificationType.Error;

    public ToastViewModel(INotificationService notifications, ISettingsService settings)
    {
        _notifications = notifications;
        _settings = settings;
        ApplyPosition();
        _notifications.StateChanged += OnStateChanged;
    }

    /// <summary>Re-reads the saved corner, so a change made in Settings applies to the next toast.</summary>
    public void ApplyPosition()
    {
        var pos = _settings.Settings.ToastPosition;
        HorizontalAlignment = pos is ToastPosition.TopLeft or ToastPosition.BottomLeft
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        VerticalAlignment = pos is ToastPosition.TopLeft or ToastPosition.TopRight
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
    }

    private void OnStateChanged(NotificationState state) => Dispatcher.UIThread.Post(() =>
    {
        if (state.IsVisible) ApplyPosition();
        IsVisible = state.IsVisible;
        Message = state.Message;
        Type = state.Type;

        if (state.IsVisible && state.Type != NotificationType.Progress)
            _ = HideAfterDelayAsync(++_generation);
    });

    // A newer toast restarts the timer instead of being cut short by the previous one.
    private async Task HideAfterDelayAsync(int generation)
    {
        await Task.Delay(AutoHideDelayMs);
        if (generation == _generation) _notifications.Hide();
    }
}
