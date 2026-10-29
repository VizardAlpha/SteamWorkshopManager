using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Core.Steam;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Telemetry;

namespace SteamWorkshopManager.ViewModels;

public partial class AddSessionViewModel : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<AddSessionViewModel>();

    /// <summary>
    /// Matches the AppId in any common Steam URL variant: store, community hub,
    /// community workshop pages, and the short <c>s.team</c> shape.
    /// </summary>
    private static readonly Regex SteamUrlRegex = new(
        @"(?:store\.steampowered\.com/app|steamcommunity\.com/app|s\.team/a)/(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly AppIdValidator _validator;
    private readonly SteamImageCache _images;
    private readonly ISessionRepository _sessionRepository;
    private readonly SessionManager _sessionManager;
    private readonly ITelemetryService _telemetry;

    [ObservableProperty]
    private string _appIdInput = string.Empty;

    [ObservableProperty]
    private bool _isValidating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private bool _isValid;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string? _gameName;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isCreating;

    [ObservableProperty]
    private uint _validatedAppId;

    [ObservableProperty]
    private Bitmap? _gameHeaderImage;

    /// <summary>
    /// Dispose the previous preview bitmap - the validate+preview flow can
    /// refetch multiple times in the same session (user edits the AppId).
    /// </summary>
    partial void OnGameHeaderImageChanging(Bitmap? value) => _gameHeaderImage?.Dispose();

    /// <summary>Platform the new session publishes to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSteam), nameof(IsModio), nameof(CanCreate))]
    private WorkshopPlatform _platform = WorkshopPlatform.Steam;

    public bool IsSteam => Platform == WorkshopPlatform.Steam;

    public bool IsModio => Platform == WorkshopPlatform.ModIo;

    public ModioSessionSetupViewModel Modio { get; }

    public bool CanCreate => IsSteam ? IsValid : Modio.IsReady;

    [RelayCommand]
    private void SelectPlatform(WorkshopPlatform platform) => Platform = platform;

    partial void OnPlatformChanged(WorkshopPlatform value)
    {
        HasError = false;
        ErrorMessage = null;
    }

    public event Action? SessionCreated;
    public event Action? CancelRequested;

    public AddSessionViewModel(
        ISessionRepository sessionRepository,
        AppIdValidator validator,
        SessionManager sessionManager,
        ITelemetryService telemetry,
        SteamImageCache images,
        ModioSessionSetupViewModel modio)
    {
        _sessionRepository = sessionRepository;
        _validator = validator;
        _sessionManager = sessionManager;
        _telemetry = telemetry;
        _images = images;
        Modio = modio;
        Modio.ReadinessChanged += () => OnPropertyChanged(nameof(CanCreate));
    }

    /// <summary>
    /// Extracts an AppId from either a plain numeric string or a Steam URL
    /// (<c>store.steampowered.com/app/…</c>, <c>steamcommunity.com/app/…</c>,
    /// <c>s.team/a/…</c>). Returns <c>false</c> when nothing recognisable is found.
    /// </summary>
    private static bool TryExtractAppId(string input, out uint appId)
    {
        appId = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var trimmed = input.Trim();
        if (uint.TryParse(trimmed, out appId)) return true;

        var match = SteamUrlRegex.Match(trimmed);
        return match.Success && uint.TryParse(match.Groups[1].Value, out appId);
    }

    partial void OnAppIdInputChanged(string value)
    {
        // Any edit invalidates the last preview - resets the card + unlocks the
        // Validate button so the user can't submit a mismatched pair.
        if (IsValid || HasError)
        {
            IsValid = false;
            HasError = false;
            ErrorMessage = null;
            GameName = null;
            GameHeaderImage = null;
            ValidatedAppId = 0;
        }
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

        if (!TryExtractAppId(AppIdInput, out var appId))
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
        GameHeaderImage = null;

        try
        {
            Log.Debug($"Validating AppId: {appId}");
            var result = await _validator.ValidateAsync(appId);

            if (result.IsValid)
            {
                IsValid = true;
                GameName = !string.IsNullOrEmpty(result.GameName) ? result.GameName : $"Game {appId}";
                ValidatedAppId = appId;
                Log.Debug($"AppId valid: {GameName}");

                // Fire-and-forget the header image so the preview card paints
                // the big picture without blocking the validation spinner.
                _ = LoadHeaderPreviewAsync(appId);
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

    private async Task LoadHeaderPreviewAsync(uint appId)
    {
        try
        {
            GameHeaderImage = await _images.GetHeaderAsync(appId);
        }
        catch (Exception ex)
        {
            Log.Debug($"Preview image load failed for AppId {appId}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreateSessionAsync()
    {
        if (!CanCreate) return;

        IsCreating = true;

        try
        {
            if (IsModio)
            {
                var modio = await _sessionManager.CreateModioSessionAsync(Modio.SelectedGame!);
                await _sessionRepository.SetActiveSessionAsync(modio.Id);
                SessionCreated?.Invoke();
                return;
            }

            if (ValidatedAppId == 0 || string.IsNullOrEmpty(GameName)) return;

            Log.Info($"Creating session for {GameName} (AppId: {ValidatedAppId})");

            var session = await _sessionManager.CreateSteamSessionAsync(ValidatedAppId, GameName);
            await _sessionRepository.SetActiveSessionAsync(session.Id);
            await SessionManager.UpdateSteamAppIdFileAsync(ValidatedAppId);

            Log.Debug("Session created successfully");

            _telemetry.Track(TelemetryEventTypes.SessionAdded, ValidatedAppId);

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

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke();
    }
}
