using CommunityToolkit.Mvvm.ComponentModel;
using SteamWorkshopManager.Services;

namespace SteamWorkshopManager.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Provides access to localized strings in code-behind.
    /// </summary>
    protected static LocalizationService Loc => LocalizationService.Instance;
}
