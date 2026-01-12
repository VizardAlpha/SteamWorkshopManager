using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services;

namespace SteamWorkshopManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<SettingsViewModel>();

    private readonly ISettingsService _settingsService;
    private readonly ISessionRepository _sessionRepository;
    private readonly SessionManager _sessionManager;
    private readonly ILogService _logService;

    [ObservableProperty]
    private bool _isEnglishSelected;

    [ObservableProperty]
    private bool _isFrenchSelected;

    [ObservableProperty]
    private bool _isDebugModeEnabled;

    [ObservableProperty]
    private string _logFilePath = string.Empty;

    [ObservableProperty]
    private WorkshopSession? _activeSession;

    [ObservableProperty]
    private ObservableCollection<WorkshopSession> _sessions = [];

    [ObservableProperty]
    private bool _isLoadingSessions;

    public event Action? CloseRequested;
    public event Action? AddSessionRequested;

    public SettingsViewModel(ISettingsService settingsService, ISessionRepository? sessionRepository = null)
    {
        _settingsService = settingsService;
        _sessionRepository = sessionRepository ?? new SessionRepository(settingsService);
        _sessionManager = new SessionManager(_sessionRepository);
        _logService = LogService.Instance;

        // Initialize selection based on current language
        var currentLang = _settingsService.Settings.Language;
        _isEnglishSelected = currentLang == "en";
        _isFrenchSelected = currentLang == "fr";

        // Initialize debug mode from settings
        _isDebugModeEnabled = _settingsService.Settings.DebugMode;
        _logFilePath = _logService.GetLogFilePath();

        // Load sessions
        _ = LoadSessionsAsync();
    }

    private async Task LoadSessionsAsync()
    {
        IsLoadingSessions = true;
        try
        {
            var sessions = await _sessionRepository.GetAllSessionsAsync();
            var activeId = AppConfig.CurrentSession?.Id;

            foreach (var session in sessions)
            {
                session.IsActive = session.Id == activeId;
            }

            Sessions = new ObservableCollection<WorkshopSession>(sessions);
            ActiveSession = AppConfig.CurrentSession;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load sessions", ex);
        }
        finally
        {
            IsLoadingSessions = false;
        }
    }

    [RelayCommand]
    private async Task SwitchSessionAsync(WorkshopSession session)
    {
        if (session.Id == ActiveSession?.Id) return;

        Log.Info($"Switching to session: {session.Name}");
        await _sessionManager.SwitchSessionAsync(session);
        // App will restart
    }

    [RelayCommand]
    private void AddSession()
    {
        AddSessionRequested?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(WorkshopSession session)
    {
        if (session.Id == ActiveSession?.Id)
        {
            Log.Warning("Cannot delete active session");
            return;
        }

        Log.Info($"Deleting session: {session.Name}");
        await _sessionRepository.DeleteSessionAsync(session.Id);
        Sessions.Remove(session);
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
