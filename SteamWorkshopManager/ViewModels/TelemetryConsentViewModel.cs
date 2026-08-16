using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
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
/// clicking Continue exits the app - there is no implicit consent path.
/// </summary>
public partial class TelemetryConsentViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<TelemetryConsentViewModel>();

    private const string PrivacyPolicyUrl = "https://swm-stats.com/Privacy";
    private const string TermsOfUseUrl = "https://swm-stats.com/Terms";

    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyPropertyChangedFor(nameof(RequiresLegalAcceptance))]
    private bool _isTelemetryEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isPrivacyAccepted;

    public event Action? ContinueRequested;

    /// <summary>
    /// Discord opt-in, surfaced here because this policy update is what
    /// introduced the feature. Enabling picks <see cref="DiscordPresenceMode.Minimal"/>,
    /// the least revealing level; it is widened in Settings > Customization.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyPropertyChangedFor(nameof(RequiresLegalAcceptance))]
    private bool _isDiscordPresenceEnabled;

    /// <summary>
    /// The Terms and the privacy policy cover both channels, so the
    /// acknowledgement is asked once, as soon as either one is enabled.
    /// </summary>
    public bool RequiresLegalAcceptance => IsTelemetryEnabled || IsDiscordPresenceEnabled;

    partial void OnIsTelemetryEnabledChanged(bool value) => DropAcceptanceWhenNothingEnabled();

    partial void OnIsDiscordPresenceEnabledChanged(bool value) => DropAcceptanceWhenNothingEnabled();

    /// <summary>
    /// Turning both opt-ins off clears the acknowledgement, so re-enabling one
    /// asks for it again instead of silently reusing the earlier tick.
    /// </summary>
    private void DropAcceptanceWhenNothingEnabled()
    {
        if (!RequiresLegalAcceptance) IsPrivacyAccepted = false;
    }

    public TelemetryConsentViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _isTelemetryEnabled = settingsService.Settings.TelemetryEnabled;
        _isDiscordPresenceEnabled = settingsService.Settings.DiscordPresenceMode != DiscordPresenceMode.Off;
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

    private bool CanContinue => !RequiresLegalAcceptance || IsPrivacyAccepted;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        _settingsService.Settings.TelemetryEnabled = IsTelemetryEnabled;
        _settingsService.Settings.TelemetryConsentVersion = TelemetryConsent.RequiredVersion;

        // Keep an already-chosen detail level rather than resetting it here.
        if (IsDiscordPresenceEnabled)
        {
            if (_settingsService.Settings.DiscordPresenceMode == DiscordPresenceMode.Off)
                _settingsService.Settings.DiscordPresenceMode = DiscordPresenceMode.Minimal;
        }
        else
        {
            _settingsService.Settings.DiscordPresenceMode = DiscordPresenceMode.Off;
        }

        _settingsService.Save();
        Log.Info($"Telemetry consent committed: enabled={IsTelemetryEnabled}, version={TelemetryConsent.RequiredVersion}");
        ContinueRequested?.Invoke();
    }
}