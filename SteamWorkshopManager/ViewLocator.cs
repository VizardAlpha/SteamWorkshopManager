using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SteamWorkshopManager.ViewModels;
using SteamWorkshopManager.Views;

namespace SteamWorkshopManager;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        // Map ViewModels to Views explicitly for AOT compatibility
        return param switch
        {
            ItemListViewModel => new ItemListView(),
            ItemEditorViewModel => new ItemEditorView(),
            CreateItemViewModel => new CreateItemView(),
            _ => new TextBlock { Text = $"View not found for: {param.GetType().Name}" }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
