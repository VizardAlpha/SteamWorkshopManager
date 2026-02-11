using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services;

public class LocalizationService : INotifyPropertyChanged
{
    private static readonly Logger Log = LogService.GetLogger<LocalizationService>();
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private const string DefaultLanguage = "en-US";
    private string _currentLanguage = DefaultLanguage;
    private ResourceDictionary? _dynamicLanguageResource;

    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; private set; } = [];

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

    public string this[string key] => GetString(key);

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
        BundleService.EnsureBundleExtracted();
        AvailableLanguages = BundleService.DiscoverLanguages();

        // Validate that current language exists in discovered languages
        if (AvailableLanguages.All(l => l.Code != _currentLanguage))
        {
            _currentLanguage = DefaultLanguage;
        }

        if (_currentLanguage != DefaultLanguage)
        {
            UpdateLanguageResources();
        }
    }

    private void UpdateLanguageResources()
    {
        if (Application.Current?.Resources is not ResourceDictionary appResources) return;

        try
        {
            // Remove previously added dynamic language resource
            if (_dynamicLanguageResource != null)
            {
                appResources.MergedDictionaries.Remove(_dynamicLanguageResource);
                _dynamicLanguageResource = null;
            }

            // Default language is already loaded from App.axaml
            if (_currentLanguage == DefaultLanguage) return;

            var langInfo = AvailableLanguages.FirstOrDefault(l => l.Code == _currentLanguage);
            if (langInfo is null) return;

            var entries = BundleService.ParseLanguageFile(langInfo.FilePath);
            _dynamicLanguageResource = new ResourceDictionary();
            foreach (var (key, value) in entries)
            {
                _dynamicLanguageResource[key] = value;
            }

            appResources.MergedDictionaries.Add(_dynamicLanguageResource);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load language resources: {ex.Message}");
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
