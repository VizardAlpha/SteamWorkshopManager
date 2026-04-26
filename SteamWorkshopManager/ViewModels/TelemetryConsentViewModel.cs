using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Telemetry;

namespace SteamWorkshopManager.ViewModels;

/// <summary>
/// Backs the consent modal shown to users upgrading from a version that
/// predates the public stats dashboard. The toggle's pre-selected value
/// mirrors whatever the user had before (legacy default was true), but the
/// "Continue" button stays disabled until the user explicitly acknowledges
/// the Terms and Privacy when telemetry is on. Closing the window without
/// clicking Continue exits the app — there is no implicit consent path.
/// </summary>
public partial class TelemetryConsentViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<TelemetryConsentViewModel>();

    private const string PrivacyPolicyUrl = "https://swm-stats.com/Privacy";
    private const string TermsOfUseUrl = "https://swm-stats.com/Terms";

    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isTelemetryEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isPrivacyAccepted;

    public event Action? ContinueRequested;

    public TelemetryConsentViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _isTelemetryEnabled = settingsService.Settings.TelemetryEnabled;
    }

    [RelayCommand]
    private static void OpenPrivacyPolicy() => OpenUrl(PrivacyPolicyUrl);

    [RelayCommand]
    private static void OpenTermsOfUse() => OpenUrl(TermsOfUseUrl);

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Debug($"Failed to open URL {url}: {ex.Message}"); }
    }

    private bool CanContinue => !IsTelemetryEnabled || IsPrivacyAccepted;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        _settingsService.Settings.TelemetryEnabled = IsTelemetryEnabled;
        _settingsService.Settings.TelemetryConsentVersion = TelemetryConsent.RequiredVersion;
        _settingsService.Save();
        Log.Info($"Telemetry consent committed: enabled={IsTelemetryEnabled}, version={TelemetryConsent.RequiredVersion}");
        ContinueRequested?.Invoke();
    }
}