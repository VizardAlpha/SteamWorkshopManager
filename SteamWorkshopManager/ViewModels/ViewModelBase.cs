using CommunityToolkit.Mvvm.ComponentModel;
using WorkshopManager.Services.Core;

namespace WorkshopManager.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Provides access to localized strings in XAML bindings and code-behind.
    /// </summary>
    public static LocalizationService Loc => LocalizationService.Instance;
}
