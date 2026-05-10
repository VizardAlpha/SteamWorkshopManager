using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Telemetry;

namespace SteamWorkshopManager.ViewModels;

public partial class SetupWizardViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<SetupWizardViewModel>();

    private const string StatsSiteUrl = "https://swm-stats.com";
    private const string PrivacyPolicyUrl = "https://swm-stats.com/Privacy";
    private const string TermsOfUseUrl = "https://swm-stats.com/Terms";

    private readonly AppIdValidator _validator;
    private readonly ISessionRepository _sessionRepository;
    private readonly SessionManager _sessionManager;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private string _appIdInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidateAppIdCommand))]
    private bool _isValidating;

    /// <summary>
    /// Gate inside the Telemetry consent box. Only required when telemetry
    /// is enabled — if the user opts out, no data leaves the machine, so
    /// the Terms/Privacy acknowledgement does not apply to validation.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidateAppIdCommand))]
    private bool _isPrivacyAccepted;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string? _gameName;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isCreating;

    /// <summary>
    /// First-run telemetry consent. Committed to settings only when the user
    /// clicks "Create session" — until then, no state leaks out of the wizard
    /// and no Track/Flush call can fire (App.axaml.cs defers AppStart).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidateAppIdCommand))]
    private bool _isTelemetryEnabled;

    private uint _validatedAppId;

    public event Action? SessionCreated;

    public SetupWizardViewModel(
        ISessionRepository sessionRepository,
        AppIdValidator validator,
        SessionManager sessionManager,
        ISettingsService settingsService)
    {
        _sessionRepository = sessionRepository;
        _validator = validator;
        _sessionManager = sessionManager;
        _settingsService = settingsService;

        // Mirror the existing stored preference so reopening the wizard
        // (via --force-setup-wizard) pre-fills the user's last choice.
        _isTelemetryEnabled = _settingsService.Settings.TelemetryEnabled;
    }

    [RelayCommand]
    private static void OpenStatsSite() => OpenUrl(StatsSiteUrl);

    [RelayCommand]
    private static void OpenPrivacyPolicy() => OpenUrl(PrivacyPolicyUrl);

    [RelayCommand]
    private static void OpenTermsOfUse() => OpenUrl(TermsOfUseUrl);

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Debug($"Failed to open URL {url}: {ex.Message}"); }
    }

    private bool CanValidate => !IsValidating && (!IsTelemetryEnabled || IsPrivacyAccepted);

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ValidateAppIdAsync()
    {
        if (string.IsNullOrWhiteSpace(AppIdInput))
        {
            HasError = true;
            ErrorMessage = Loc["AppIdRequired"];
            return;
        }

        // Accept either a raw AppID or a Steam Store URL so first-time users
        // can paste the full store link and not hunt for the numeric id.
        if (!AppIdValidator.TryParseAppId(AppIdInput, out var appId))
        {
            HasError = true;
            ErrorMessage = Loc["AppIdInvalid"];
            return;
        }

        IsValidating = true;
        HasError = false;
        IsValid = false;
        ErrorMessage = null;
        GameName = null;

        try
        {
            Log.Info($"Validating AppId: {appId}");
            var result = await _validator.ValidateAsync(appId);

            if (result.IsValid)
            {
                IsValid = true;
                GameName = !string.IsNullOrEmpty(result.GameName) ? result.GameName : $"Game {appId}";
                _validatedAppId = appId;
                Log.Info($"AppId valid: {GameName}");
            }
            else
            {
                HasError = true;
                ErrorMessage = result.ErrorKey != null ? Loc[result.ErrorKey] : result.ErrorMessage;
                Log.Warning($"AppId invalid: {result.ErrorKey}");
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            Log.Error("Validation error", ex);
        }
        finally
        {
            IsValidating = false;
        }
    }

    [RelayCommand]
    private async Task CreateSessionAsync()
    {
        if (!IsValid || _validatedAppId == 0 || string.IsNullOrEmpty(GameName))
        {
            return;
        }

        IsCreating = true;

        try
        {
            Log.Info($"Creating session for {GameName} (AppId: {_validatedAppId})");

            // Commit the user's telemetry choice BEFORE anything else so the
            // AppStart event that App.axaml.cs fires next honors consent.
            // The consent version is stamped here too so the upgrade modal in
            // App.axaml.cs does not fire at the very first launch right after
            // the wizard closes.
            _settingsService.Settings.TelemetryEnabled = IsTelemetryEnabled;
            _settingsService.Settings.TelemetryConsentVersion = TelemetryConsent.RequiredVersion;
            _settingsService.Save();

            // Create the session
            var session = await _sessionManager.CreateSessionAsync(_validatedAppId, GameName);

            // Set it as active
            await _sessionRepository.SetActiveSessionAsync(session.Id);

            // Update steam_appid.txt
            await SessionManager.UpdateSteamAppIdFileAsync(_validatedAppId);

            Log.Info("Session created successfully");

            // Notify that session was created
            SessionCreated?.Invoke();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            Log.Error("Failed to create session", ex);
        }
        finally
        {
            IsCreating = false;
        }
    }
}
