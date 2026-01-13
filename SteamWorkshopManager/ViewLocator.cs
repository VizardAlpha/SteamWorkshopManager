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

        // Map ViewModels
        return param switch
        {
            ItemListViewModel => new ItemListView(),
            ItemEditorViewModel => new ItemEditorView(),
            CreateItemViewModel => new CreateItemView(),
            SettingsViewModel => new SettingsView(),
            _ => new TextBlock { Text = $"View not found for: {param.GetType().Name}" }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
