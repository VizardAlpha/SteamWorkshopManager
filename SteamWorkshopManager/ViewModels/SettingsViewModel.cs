using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Services;

namespace SteamWorkshopManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILogService _logService;

    [ObservableProperty]
    private bool _isEnglishSelected;

    [ObservableProperty]
    private bool _isFrenchSelected;

    [ObservableProperty]
    private bool _isDebugModeEnabled;

    [ObservableProperty]
    private string _logFilePath = string.Empty;

    public event Action? CloseRequested;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _logService = LogService.Instance;

        // Initialize selection based on current language
        var currentLang = _settingsService.Settings.Language;
        _isEnglishSelected = currentLang == "en";
        _isFrenchSelected = currentLang == "fr";

        // Initialize debug mode from settings
        _isDebugModeEnabled = _settingsService.Settings.DebugMode;
        _logFilePath = _logService.GetLogFilePath();
    }

    partial void OnIsDebugModeEnabledChanged(bool value)
    {
        _settingsService.Settings.DebugMode = value;
        _settingsService.Save();
        _logService.SetDebugMode(value);
    }

    [RelayCommand]
    private void SelectEnglish()
    {
        IsEnglishSelected = true;
        IsFrenchSelected = false;
        SetLanguage("en");
    }

    [RelayCommand]
    private void SelectFrench()
    {
        IsEnglishSelected = false;
        IsFrenchSelected = true;
        SetLanguage("fr");
    }

    private void SetLanguage(string lang)
    {
        _settingsService.Settings.Language = lang;
        _settingsService.Save();
        LocalizationService.Instance.CurrentLanguage = lang;
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        var logFolder = Path.GetDirectoryName(LogFilePath);
        if (string.IsNullOrEmpty(logFolder) || !Directory.Exists(logFolder)) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{LogFilePath}\"",
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"-R \"{LogFilePath}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", logFolder);
            }
        }
        catch
        {
            // Ignore errors opening folder
        }
    }
}
