using System;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkshopManager.Models;
using WorkshopManager.Services.Core;
using WorkshopManager.Services.Steam;

namespace WorkshopManager.ViewModels.Shell;

/// <summary>Connection dot and status line of the footer, for the active session's platform.</summary>
public partial class ConnectionStatusViewModel : ViewModelBase
{
    private string _statusKey = "ConnectingToSteam";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    [NotifyPropertyChangedFor(nameof(IsStandby))]
    private SteamConnectionState _state = SteamConnectionState.Connecting;

    [ObservableProperty]
    private string _statusMessage;

    public bool IsConnecting => State == SteamConnectionState.Connecting;
    public bool IsConnected => State == SteamConnectionState.Connected;
    public bool IsDisconnected => State == SteamConnectionState.Disconnected;
    public bool IsStandby => State == SteamConnectionState.Standby;

    public event Action<SteamConnectionState>? StateChanged;

    public ConnectionStatusViewModel()
    {
        _statusMessage = Loc[_statusKey];
        LocalizationService.Instance.LanguageChanged += () => StatusMessage = Loc[_statusKey];
    }

    public void SetConnecting() => Set(SteamConnectionState.Connecting, "ConnectingToSteam");

    /// <summary>Steam was released on purpose; the next action reconnects.</summary>
    public void SetStandby() => Set(SteamConnectionState.Standby, "SteamStandby");

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

    /// <summary>mod.io has no live connection: the session is usable once signed in.</summary>
    public void ApplyModio(bool signedIn) => Set(
        signedIn ? SteamConnectionState.Connected : SteamConnectionState.Disconnected,
        signedIn ? "ConnectedToModio" : "ModioNotSignedIn");

    partial void OnStateChanged(SteamConnectionState value) => StateChanged?.Invoke(value);

    private void Set(SteamConnectionState state, string statusKey)
    {
        _statusKey = statusKey;
        StatusMessage = Loc[statusKey];
        State = state;
    }
}
