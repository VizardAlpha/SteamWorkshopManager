using CommunityToolkit.Mvvm.ComponentModel;

namespace SteamWorkshopManager.Models;

public partial class AppDependencyInfo : ObservableObject
{
    public uint AppId { get; set; }
    public string? Name { get; set; }

    [ObservableProperty]
    private bool _isRemoving;

    public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : $"AppID: {AppId}";
    public string StoreUrl => $"https://store.steampowered.com/app/{AppId}";
}
