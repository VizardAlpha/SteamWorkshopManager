using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SteamWorkshopManager.Models;

public partial class TagCategory : ObservableObject
{
    public required string Name { get; init; }
    public bool IsDropdown { get; init; }
    public ObservableCollection<WorkshopTag> Tags { get; init; } = [];

    [ObservableProperty]
    private WorkshopTag? _selectedTag;

    [RelayCommand]
    private void ClearSelectedTag() => SelectedTag = null;

    partial void OnSelectedTagChanged(WorkshopTag? value)
    {
        // For dropdown categories: only the selected tag should be marked
        if (!IsDropdown) return;
        foreach (var tag in Tags)
            tag.IsSelected = tag == value;
    }

    /// <summary>
    /// Initializes SelectedTag from whichever tag has IsSelected=true.
    /// Call after populating Tags.
    /// </summary>
    public void SyncSelectedTag()
    {
        if (IsDropdown)
            SelectedTag = Tags.FirstOrDefault(t => t.IsSelected);
    }
}
