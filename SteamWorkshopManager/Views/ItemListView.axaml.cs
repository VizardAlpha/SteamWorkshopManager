using Avalonia.Controls;
using WorkshopManager.Models;
using WorkshopManager.ViewModels;

namespace WorkshopManager.Views;

public partial class ItemListView : UserControl
{
    public ItemListView()
    {
        InitializeComponent();

        // The grid only realizes visible cards: each one asks for its thumbnail when it appears.
        ModGrid.ElementPrepared += (_, e) =>
        {
            // Read the item from the source: the element's DataContext may not be set yet.
            if (ModGrid.ItemsSourceView?.GetAt(e.Index) is WorkshopItem item && DataContext is ItemListViewModel vm)
                vm.RequestThumbnail(item);
        };
    }
}
