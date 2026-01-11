using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;

namespace SteamWorkshopManager.Services;

public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    // Default language is "en" - must match the default ResourceInclude in App.axaml
    private const string DefaultLanguage = "en";
    private string _currentLanguage = DefaultLanguage;
    private ResourceInclude? _dynamicLanguageResource;

    private static readonly Dictionary<string, string> LanguageFiles = new()
    {
        ["en"] = "avares://SteamWorkshopManager/Resources/Languages/en-US.axaml",
        ["fr"] = "avares://SteamWorkshopManager/Resources/Languages/fr-FR.axaml"
    };

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                UpdateLanguageResources();
                OnPropertyChanged();
                LanguageChanged?.Invoke();
            }
        }
    }

    public event Action? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets a localized string from the current language resources.
    /// </summary>
    public string this[string key] => GetString(key);

    /// <summary>
    /// Gets a localized string from the current language resources.
    /// </summary>
    public static string GetString(string key)
    {
        if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true)
        {
            return value as string ?? key;
        }
        return key;
    }

    public void Initialize()
    {
        // Only update if not using the default language (which is already loaded from App.axaml)
        if (_currentLanguage != DefaultLanguage)
        {
            UpdateLanguageResources();
        }
    }

#pragma warning disable IL2026 // Safe for desktop apps without trimming
    private void UpdateLanguageResources()
    {
        if (Application.Current?.Resources is not ResourceDictionary appResources) return;

        try
        {
            // Remove previously added dynamic language resource
            if (_dynamicLanguageResource != null)
            {
                if (appResources.MergedDictionaries.Contains(_dynamicLanguageResource))
                {
                    appResources.MergedDictionaries.Remove(_dynamicLanguageResource);
                }
                _dynamicLanguageResource = null;
            }

            // If switching to default language, the static resource from App.axaml is already there
            if (_currentLanguage == DefaultLanguage)
            {
                return;
            }

            // Add new language resource on top (it will override the default)
            if (LanguageFiles.TryGetValue(_currentLanguage, out var resourcePath))
            {
                _dynamicLanguageResource = new ResourceInclude(new Uri("avares://SteamWorkshopManager"))
                {
                    Source = new Uri(resourcePath)
                };
                appResources.MergedDictionaries.Add(_dynamicLanguageResource);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to load language resources: {ex.Message}");
        }
    }
#pragma warning restore IL2026

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
