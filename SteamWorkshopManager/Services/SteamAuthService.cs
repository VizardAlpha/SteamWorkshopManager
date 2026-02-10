using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Authentication;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Handles SteamKit2 QR-based authentication to obtain web tokens
/// for authenticated requests to steamcommunity.com.
/// </summary>
public static class SteamAuthService
{
    private static readonly Logger Log = new("SteamAuthService", LogService.Instance);

    private static string? _accessToken;
    private static string? _refreshToken;
    private static string? _accountName;
    private static ulong _steamId64;
    private static ISettingsService? _settingsService;

    public static bool IsAuthenticated => _accessToken != null && _steamId64 != 0;
    public static bool HasRefreshToken => !string.IsNullOrEmpty(_refreshToken) && !string.IsNullOrEmpty(_accountName);

    /// <summary>
    /// Fired when the QR challenge URL changes (UI should re-render the QR code).
    /// </summary>
    public static event Action<string>? QrChallengeUrlChanged;

    /// <summary>
    /// Load stored tokens from settings. Reuses the access token if still valid.
    /// </summary>
    public static void Initialize(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _refreshToken = settingsService.Settings.SteamRefreshToken;
        _accountName = settingsService.Settings.SteamAccountName;
        _steamId64 = settingsService.Settings.SteamId64;

        // Restore persisted access token if it hasn't expired
        var savedAccessToken = settingsService.Settings.SteamAccessToken;
        if (!string.IsNullOrEmpty(savedAccessToken) && !IsJwtExpired(savedAccessToken))
        {
            _accessToken = savedAccessToken;
            Log.Info($"Restored valid access token for account={_accountName}, steamId64={_steamId64}");
        }
        else
        {
            Log.Info($"Initialized: hasRefreshToken={HasRefreshToken}, account={_accountName}, steamId64={_steamId64}, accessToken={(savedAccessToken != null ? "expired" : "none")}");
        }
    }

    /// <summary>
    /// Try to refresh the access token using the stored refresh token.
    /// Connects to CM, logs on with the refresh token, then generates a new web access token.
    /// Returns true if a valid access token is now available.
    /// </summary>
    public static async Task<bool> TryRefreshAccessTokenAsync()
    {
        if (IsAuthenticated)
            return true;

        if (!HasRefreshToken || _steamId64 == 0)
            return false;

        Log.Info($"Attempting to refresh access token for account {_accountName}...");

        SteamClient? client = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            client = new SteamClient();
            var manager = new CallbackManager(client);
            var steamUser = client.GetHandler<SteamUser>()!;

            var connectedTcs = new TaskCompletionSource<bool>();
            var loggedOnTcs = new TaskCompletionSource<SteamUser.LoggedOnCallback>();

            manager.Subscribe<SteamClient.ConnectedCallback>(_ => connectedTcs.TrySetResult(true));
            manager.Subscribe<SteamClient.DisconnectedCallback>(_ => connectedTcs.TrySetResult(false));
            manager.Subscribe<SteamUser.LoggedOnCallback>(cb => loggedOnTcs.TrySetResult(cb));

            StartCallbackLoop(manager, cts.Token);

            client.Connect();

            if (!await connectedTcs.Task)
            {
                Log.Warning("Failed to connect to Steam CM servers");
                return false;
            }

            // Log on with the stored refresh token
            Log.Debug("Connected to CM, logging on with refresh token...");
            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = _accountName,
                AccessToken = _refreshToken,
                LoginID = 0x534B3200,
            });

            var logOnResult = await loggedOnTcs.Task;

            if (logOnResult.Result != EResult.OK)
            {
                Log.Warning($"LogOn with refresh token failed: {logOnResult.Result}");
                // Token is likely expired or revoked
                if (logOnResult.Result is EResult.InvalidPassword or EResult.AccessDenied
                    or EResult.Expired or EResult.Revoked)
                {
                    Log.Info("Clearing expired/revoked tokens");
                    ClearTokens();
                }
                return false;
            }

            Log.Debug("Logged on, generating web access token...");

            // Generate a fresh web access token
            var tokenResult = await client.Authentication.GenerateAccessTokenForAppAsync(
                logOnResult.ClientSteamID!, _refreshToken!, true);

            _accessToken = tokenResult.AccessToken;

            if (!string.IsNullOrEmpty(tokenResult.RefreshToken))
            {
                _refreshToken = tokenResult.RefreshToken;
                SaveTokens();
            }

            Log.Info("Access token refreshed successfully");

            steamUser.LogOff();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to refresh access token: {ex.Message}");
            _accessToken = null;
            return false;
        }
        finally
        {
            await cts.CancelAsync();
            client?.Disconnect();
        }
    }

    /// <summary>
    /// Start QR auth flow. Fires QrChallengeUrlChanged with the URL to render.
    /// Blocks until the user scans and approves, or cancellation.
    /// </summary>
    public static async Task BeginQrAuthAsync(CancellationToken ct)
    {
        Log.Info("Starting QR auth flow...");

        SteamClient? client = null;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            client = new SteamClient();
            var manager = new CallbackManager(client);

            var connectedTcs = new TaskCompletionSource<bool>();
            manager.Subscribe<SteamClient.ConnectedCallback>(_ => connectedTcs.TrySetResult(true));
            manager.Subscribe<SteamClient.DisconnectedCallback>(_ => connectedTcs.TrySetResult(false));

            StartCallbackLoop(manager, linkedCts.Token);

            client.Connect();

            if (!await connectedTcs.Task)
                throw new InvalidOperationException("Failed to connect to Steam CM servers");

            Log.Debug("Connected to CM, starting QR auth session...");

            var authSession = await client.Authentication.BeginAuthSessionViaQRAsync(
                new AuthSessionDetails
                {
                    DeviceFriendlyName = "SteamWorkshopManager",
                    IsPersistentSession = true,
                });

            // Fire initial QR URL
            QrChallengeUrlChanged?.Invoke(authSession.ChallengeURL);

            // Steam periodically refreshes the challenge URL
            authSession.ChallengeURLChanged = () =>
            {
                QrChallengeUrlChanged?.Invoke(authSession.ChallengeURL);
            };

            Log.Debug("Waiting for user to scan QR code...");
            var pollResult = await authSession.PollingWaitForResultAsync(linkedCts.Token);
            Log.Info($"QR auth successful for account: {pollResult.AccountName}");

            // Now log on to get the SteamID
            var steamUser = client.GetHandler<SteamUser>()!;
            var loggedOnTcs = new TaskCompletionSource<SteamUser.LoggedOnCallback>();

            manager.Subscribe<SteamUser.LoggedOnCallback>(cb => loggedOnTcs.TrySetResult(cb));

            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = pollResult.AccountName,
                AccessToken = pollResult.RefreshToken,
                LoginID = 0x534B3200,
            });

            var logOnResult = await loggedOnTcs.Task;

            if (logOnResult.Result != EResult.OK)
            {
                Log.Warning($"LogOn failed: {logOnResult.Result}");
                throw new InvalidOperationException($"Steam logon failed: {logOnResult.Result}");
            }

            _steamId64 = logOnResult.ClientSteamID!.ConvertToUInt64();
            _accountName = pollResult.AccountName;
            _accessToken = pollResult.AccessToken;
            _refreshToken = pollResult.RefreshToken;

            SaveTokens();
            Log.Info($"Auth complete. SteamID64={_steamId64}");

            steamUser.LogOff();
        }
        finally
        {
            await linkedCts.CancelAsync();
            client?.Disconnect();
        }
    }

    /// <summary>
    /// Creates an HttpClient with the steamLoginSecure cookie set.
    /// </summary>
    public static HttpClient CreateAuthenticatedHttpClient()
    {
        if (!IsAuthenticated)
            throw new InvalidOperationException("Not authenticated");

        var cookieContainer = new CookieContainer();
        var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

        cookieContainer.Add(new Uri("https://steamcommunity.com"), new Cookie("steamLoginSecure",
            $"{_steamId64}%7C%7C{_accessToken}"));
        cookieContainer.Add(new Uri("https://steamcommunity.com"), new Cookie("sessionid", sessionId));

        var handler = new HttpClientHandler { CookieContainer = cookieContainer };
        var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SteamWorkshopManager/1.0");
        return httpClient;
    }

    /// <summary>
    /// Clear all stored tokens (logout).
    /// </summary>
    private static void ClearTokens()
    {
        _accessToken = null;
        _refreshToken = null;
        _accountName = null;
        _steamId64 = 0;
        SaveTokens();
    }

    private static void SaveTokens()
    {
        if (_settingsService == null) return;
        _settingsService.Settings.SteamRefreshToken = _refreshToken;
        _settingsService.Settings.SteamAccessToken = _accessToken;
        _settingsService.Settings.SteamAccountName = _accountName;
        _settingsService.Settings.SteamId64 = _steamId64;
        _settingsService.Save();
    }

    /// <summary>
    /// Checks if a JWT access token has expired by reading the exp claim.
    /// Returns true if expired or unparseable (fail-safe).
    /// </summary>
    private static bool IsJwtExpired(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return true;

            // Decode the payload (base64url)
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("exp", out var expElement))
            {
                var exp = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
                // Consider expired if less than 5 minutes remaining
                return exp < DateTimeOffset.UtcNow.AddMinutes(5);
            }

            return true; // No exp claim → treat as expired
        }
        catch
        {
            return true; // Unparseable → treat as expired
        }
    }

    private static void StartCallbackLoop(CallbackManager manager, CancellationToken ct)
    {
        _ = Task.Run(() =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                    manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
            }
            catch (OperationCanceledException) { }
        }, ct);
    }
}
