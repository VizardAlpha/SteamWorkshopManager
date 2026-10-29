using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Platforms.Modio;

namespace SteamWorkshopManager.ViewModels;

/// <summary>
/// mod.io half of the "add a session" flows: sign in with the user's own token, then pick a game.
/// Hosted by <see cref="AddSessionViewModel"/> and <see cref="SetupWizardViewModel"/>.
/// </summary>
public partial class ModioSessionSetupViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<ModioSessionSetupViewModel>();

    private readonly ModioAccount _account;
    private readonly ModioApiClient _client;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isSearching;

    [ObservableProperty]
    private string? _searchError;

    [ObservableProperty]
    private bool _hasNoResults;

    [ObservableProperty]
    private bool _hasGames;

    [ObservableProperty]
    private ModioGame? _selectedGame;

    public ModioSignInViewModel SignIn { get; }

    public ObservableCollection<ModioGame> Games { get; } = [];

    /// <summary>Raised when the session can or can no longer be created.</summary>
    public event Action? ReadinessChanged;

    public bool IsReady => SignIn.IsSignedIn && SelectedGame is not null;

    public ModioSessionSetupViewModel(ModioAccount account, ModioApiClient client, ModioSignInViewModel signIn)
    {
        _account = account;
        _client = client;
        SignIn = signIn;
        SignIn.SignedInChanged += OnSignedInChanged;
    }

    partial void OnSelectedGameChanged(ModioGame? value) => ReadinessChanged?.Invoke();

    private void OnSignedInChanged()
    {
        if (!SignIn.IsSignedIn)
        {
            Games.Clear();
            HasGames = false;
            SelectedGame = null;
        }
        ReadinessChanged?.Invoke();
    }

    private bool CanSearch => !IsSearching;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (_account.Credentials is not { } auth || string.IsNullOrWhiteSpace(SearchQuery)) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsSearching = true;
        SearchError = null;
        HasNoResults = false;
        try
        {
            var games = await _client.SearchGamesAsync(auth, SearchQuery.Trim(), ct);
            Games.Clear();
            foreach (var game in games) Games.Add(game);
            HasGames = Games.Count > 0;
            HasNoResults = !HasGames;
            SelectedGame = Games.Count == 1 ? Games[0] : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (ModioApiException ex) when (ex.IsAuthError)
        {
            _account.Expire();
            SearchError = Loc["ModioTokenRejected"];
        }
        catch (ModioApiException ex)
        {
            SearchError = ex.IsRateLimited ? Loc["ModioRateLimited"] : $"{Loc["ModioSearchFailed"]}: {ex.Message}";
        }
        catch (Exception ex)
        {
            Log.Warning($"mod.io game search failed: {ex.Message}");
            SearchError = Loc["ModioUnreachable"];
        }
        finally
        {
            IsSearching = false;
        }
    }

    public void Dispose()
    {
        SignIn.SignedInChanged -= OnSignedInChanged;
        SignIn.Dispose();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
