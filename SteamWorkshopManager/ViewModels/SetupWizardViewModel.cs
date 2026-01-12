using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Services;

namespace SteamWorkshopManager.ViewModels;

public partial class SetupWizardViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<SetupWizardViewModel>();

    private readonly AppIdValidator _validator;
    private readonly ISessionRepository _sessionRepository;
    private readonly SessionManager _sessionManager;

    [ObservableProperty]
    private string _appIdInput = string.Empty;

    [ObservableProperty]
    private bool _isValidating;

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

    private uint _validatedAppId;

    public event Action? SessionCreated;

    public SetupWizardViewModel(ISessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
        _validator = new AppIdValidator();
        _sessionManager = new SessionManager(sessionRepository);
    }

    [RelayCommand]
    private async Task ValidateAppIdAsync()
    {
        if (string.IsNullOrWhiteSpace(AppIdInput))
        {
            HasError = true;
            ErrorMessage = Loc["AppIdRequired"];
            return;
        }

        if (!uint.TryParse(AppIdInput.Trim(), out var appId))
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
