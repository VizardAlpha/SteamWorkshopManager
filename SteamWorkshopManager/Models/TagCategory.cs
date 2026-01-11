using System.Collections.ObjectModel;

namespace SteamWorkshopManager.Models;

public class TagCategory
{
    public required string Name { get; init; }
    public ObservableCollection<WorkshopTag> Tags { get; init; } = [];
    public bool IsExpanded { get; set; }
}
