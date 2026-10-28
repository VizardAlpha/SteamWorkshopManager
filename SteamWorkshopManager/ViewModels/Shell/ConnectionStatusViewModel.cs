using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.ViewModels.Shell;

/// <summary>Steam connection dot and status line of the footer.</summary>
public partial class ConnectionStatusViewModel : ViewModelBase
{
    private string _statusKey = "ConnectingToSteam";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private SteamConnectionState _state = SteamConnectionState.Connecting;

    [ObservableProperty]
    private string _statusMessage;

    public bool IsConnecting => State == SteamConnectionState.Connecting;
    public bool IsConnected => State == SteamConnectionState.Connected;
    public bool IsDisconnected => State == SteamConnectionState.Disconnected;

    public event Action<SteamConnectionState>? StateChanged;

    public ConnectionStatusViewModel()
    {
        _statusMessage = Loc[_statusKey];
        LocalizationService.Instance.LanguageChanged += () => StatusMessage = Loc[_statusKey];
    }

    public void SetConnecting() => Set(SteamConnectionState.Connecting, "ConnectingToSteam");

    public void SetDisconnected(string statusKey = "SteamNotAvailable") => Set(SteamConnectionState.Disconnected, statusKey);

    public void Apply(SteamInitResult result) => Set(
        result == SteamInitResult.Success ? SteamConnectionState.Connected : SteamConnectionState.Disconnected,
        result switch
        {
            SteamInitResult.Success => "ConnectedToSteam",
            SteamInitResult.GameNotOwned => "GameNotOwned",
            SteamInitResult.SteamOffline => "SteamOffline",
            _ => "SteamNotAvailable",
        });

    partial void OnStateChanged(SteamConnectionState value) => StateChanged?.Invoke(value);

    private void Set(SteamConnectionState state, string statusKey)
    {
        _statusKey = statusKey;
        StatusMessage = Loc[statusKey];
        State = state;
    }
}
