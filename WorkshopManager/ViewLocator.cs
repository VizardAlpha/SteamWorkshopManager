using Avalonia.Controls;
using Avalonia.Controls.Templates;
using WorkshopManager.ViewModels;
using WorkshopManager.Views;

namespace WorkshopManager;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        // Map ViewModels
        return param switch
        {
            HomeViewModel => new HomeView(),
            ItemListViewModel => new ItemListView(),
            ItemEditorViewModel => new ItemEditorView(),
            ModioItemEditorViewModel => new ModioItemEditorView(),
            CreateItemViewModel => new CreateItemView(),
            CreateModioItemViewModel => new CreateModioItemView(),
            SettingsViewModel => new SettingsView(),
            _ => new TextBlock { Text = $"View not found for: {param.GetType().Name}" }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
