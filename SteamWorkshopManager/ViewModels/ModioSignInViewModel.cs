using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Platforms.Modio;

namespace SteamWorkshopManager.ViewModels;

/// <summary>Signs in to mod.io with the user's own token. Used by the add-session flows and the mod list.</summary>
public partial class ModioSignInViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<ModioSignInViewModel>();

    private readonly ModioAccount _account;

    [ObservableProperty]
    private string _apiPathInput = string.Empty;

    [ObservableProperty]
    private string _tokenInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isSigningIn;

    [ObservableProperty]
    private string? _signInError;

    public bool IsSignedIn => _account.IsSignedIn;

    public string? Username => _account.Username;

    /// <summary>Raised on the UI thread after a sign-in or sign-out.</summary>
    public event Action? SignedInChanged;

    public ModioSignInViewModel(ModioAccount account)
    {
        _account = account;
        _account.Changed += OnAccountChanged;
    }

    // The account may change from a background call (an expired token).
    private void OnAccountChanged() => Dispatcher.UIThread.Post(() =>
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(Username));
        SignedInChanged?.Invoke();
    });

    [RelayCommand]
    private static void OpenAccessPage()
    {
        try { Process.Start(new ProcessStartInfo(ModioAccount.AccessPageUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Debug($"Failed to open mod.io access page: {ex.Message}"); }
    }

    private bool CanSignIn => !IsSigningIn;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        IsSigningIn = true;
        SignInError = null;
        try
        {
            await _account.SignInAsync(ApiPathInput, TokenInput);
            TokenInput = string.Empty;
        }
        catch (ModioSignInException ex)
        {
            SignInError = Loc[ex.MessageKey];
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    [RelayCommand]
    private void SignOut() => _account.SignOut();

    public void Dispose() => _account.Changed -= OnAccountChanged;
}
