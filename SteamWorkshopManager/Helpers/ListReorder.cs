using System.Collections.ObjectModel;

namespace SteamWorkshopManager.Helpers;

public static class ListReorder
{
    /// <summary>
    /// Moves <paramref name="source"/> into <paramref name="target"/>'s slot,
    /// shifting the rest. Returns false when the move would change nothing or
    /// either item is missing from the list.
    /// </summary>
    public static bool Move<T>(ObservableCollection<T> list, T source, T target) where T : class
    {
        if (ReferenceEquals(source, target)) return false;

        var from = list.IndexOf(source);
        var to = list.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return false;

        list.Move(from, to);
        return true;
    }
}