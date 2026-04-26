using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<HomeViewModel>();

    public ItemListViewModel ItemList { get; }

    [ObservableProperty]
    private Bitmap? _headerImage;

    /// <summary>Dispose the old hero image on session switch so its surface is freed right away.</summary>
    partial void OnHeaderImageChanging(Bitmap? value) => _headerImage?.Dispose();

    [ObservableProperty]
    private SteamConnectionState _connectionState = SteamConnectionState.Connecting;

    [ObservableProperty]
    private bool _isRefreshingHeader;

    public HomeViewModel(ItemListViewModel itemList)
    {
        ItemList = itemList;
        ItemList.Items.CollectionChanged += OnItemsCollectionChanged;
        _ = LoadHeaderImageAsync();
    }

    public string ActiveGameName => AppConfig.CurrentSession?.GameName ?? string.Empty;

    public uint ActiveAppId => AppConfig.AppId;

    public bool HasActiveSession => AppConfig.CurrentSession is not null;

    public int ModCount => ItemList.Items.Count;

    public bool HasMods => ItemList.Items.Count > 0;

    public string TotalSubscribersDisplay =>
        Formatters.CompactNumber(ItemList.Items.Sum(i => (long)i.SubscriberCount));

    public string TotalSizeDisplay =>
        Formatters.Bytes(ItemList.Items.Sum(i => i.FileSize));

    public string LastUpdateDisplay =>
        ItemList.Items.Count == 0
            ? "—"
            : Formatters.TimeAgo(ItemList.Items.Max(i => i.UpdatedAt));

    public string ConnectionText => ConnectionState switch
    {
        SteamConnectionState.Connected => "Connected to Steam",
        SteamConnectionState.Connecting => "Connecting to Steam…",
        _ => "Steam not available",
    };

    public bool IsConnecting => ConnectionState == SteamConnectionState.Connecting;
    public bool IsConnected => ConnectionState == SteamConnectionState.Connected;
    public bool IsDisconnected => ConnectionState == SteamConnectionState.Disconnected;

    partial void OnConnectionStateChanged(SteamConnectionState value)
    {
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDisconnected));
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(ModCount));
            OnPropertyChanged(nameof(HasMods));
            OnPropertyChanged(nameof(TotalSubscribersDisplay));
            OnPropertyChanged(nameof(TotalSizeDisplay));
            OnPropertyChanged(nameof(LastUpdateDisplay));
        });
    }

    /// <summary>
    /// Called by the shell when the active session changes so the dashboard
    /// re-derives everything that keys off <c>AppConfig.CurrentSession</c>
    /// and reloads the hero image for the new AppId.
    /// </summary>
    public void OnSessionChanged()
    {
        OnPropertyChanged(nameof(ActiveGameName));
        OnPropertyChanged(nameof(ActiveAppId));
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(ModCount));
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(TotalSubscribersDisplay));
        OnPropertyChanged(nameof(TotalSizeDisplay));
        OnPropertyChanged(nameof(LastUpdateDisplay));
        _ = LoadHeaderImageAsync();
    }

    [RelayCommand]
    private async Task RefreshHeaderImageAsync()
    {
        var appId = ActiveAppId;
        if (appId == 0) return;

        IsRefreshingHeader = true;
        try
        {
            SteamImageCache.InvalidateHeader(appId);
            await LoadHeaderImageAsync(forceDownload: true);
        }
        finally
        {
            IsRefreshingHeader = false;
        }
    }

    private async Task LoadHeaderImageAsync(bool forceDownload = false)
    {
        var bitmap = await SteamImageCache.GetHeaderAsync(ActiveAppId, forceDownload);
        if (bitmap is not null)
            await Dispatcher.UIThread.InvokeAsync(() => HeaderImage = bitmap);
    }
}
