using CommunityToolkit.Mvvm.ComponentModel;

namespace SteamWorkshopManager.Models;

public partial class DependencyInfo : ObservableObject
{
    public ulong PublishedFileId { get; set; }
    public string Title { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public bool IsValid { get; set; } = true;

    [ObservableProperty]
    private bool _isRemoving;

    public string WorkshopUrl => $"https://steamcommunity.com/sharedfiles/filedetails/?id={PublishedFileId}";
}
