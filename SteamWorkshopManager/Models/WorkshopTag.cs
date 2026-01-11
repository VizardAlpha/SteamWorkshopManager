using CommunityToolkit.Mvvm.ComponentModel;

namespace SteamWorkshopManager.Models;

public partial class WorkshopTag : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public WorkshopTag() { }

    public WorkshopTag(string name, bool isSelected = false)
    {
        _name = name;
        _isSelected = isSelected;
    }
}
