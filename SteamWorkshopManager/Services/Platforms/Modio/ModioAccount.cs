using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Platforms.Modio;

internal sealed record StoredModioAccount(string BaseUrl, string Token, long UserId, string? Username);

[JsonSerializable(typeof(StoredModioAccount))]
internal partial class ModioAccountJsonContext : JsonSerializerContext;

/// <summary>Why a sign-in was refused, as a localization key.</summary>
public sealed class ModioSignInException(string messageKey, Exception? inner = null) : Exception(messageKey, inner)
{
    public string MessageKey { get; } = messageKey;
}

/// <summary>
/// The signed-in mod.io user. The token and API path are the user's own, pasted from
/// mod.io/me/access and checked against <c>/me</c> before being kept, encrypted, in the
/// machine-local folder (<see cref="ProtectedFile"/>).
/// </summary>
public sealed class ModioAccount
{
    private static readonly Logger Log = LogService.GetLogger<ModioAccount>();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SteamWorkshopManager.ModioCredentials.v1");

    public const string AccessPageUrl = "https://mod.io/me/access";

    private readonly ModioApiClient _client;
    private readonly string _filePath;

    public ModioAccount(ModioApiClient client) : this(client, AppPaths.ModioCredentialsFile) { }

    internal ModioAccount(ModioApiClient client, string filePath)
    {
        _client = client;
        _filePath = filePath;
        Restore();
    }

    public ModioCredentials? Credentials { get; private set; }

    public long UserId { get; private set; }

    public string? Username { get; private set; }

    public bool IsSignedIn => Credentials is not null;

    /// <summary>Raised after a sign-in or sign-out.</summary>
    public event Action? Changed;

    /// <summary>Checks the pasted values with mod.io, then keeps them.</summary>
    /// <exception cref="ModioSignInException">The values were refused; the key says why.</exception>
    public async Task SignInAsync(string? apiPath, string? token, CancellationToken ct = default)
    {
        if (!ModioCredentials.TryParseBaseUrl(apiPath, out var baseUrl))
            throw new ModioSignInException("ModioApiPathInvalid");
        token = token?.Trim();
        if (string.IsNullOrEmpty(token))
            throw new ModioSignInException("ModioTokenRequired");

        var credentials = new ModioCredentials(baseUrl, token);
        ModioUser me;
        try
        {
            me = await _client.GetMeAsync(credentials, ct);
        }
        catch (ModioApiException ex) when (ex.IsAuthError)
        {
            throw new ModioSignInException("ModioTokenRejected", ex);
        }
        catch (ModioApiException ex) when (ex.IsTermsRequired)
        {
            throw new ModioSignInException("ModioTermsRequired", ex);
        }
        catch (ModioApiException ex) when (ex.IsRateLimited)
        {
            throw new ModioSignInException("ModioRateLimited", ex);
        }
        catch (Exception ex) when (ex is ModioApiException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new ModioSignInException("ModioUnreachable", ex);
        }

        Apply(credentials, me.Id, me.Username ?? me.NameId);
        Save();
        Log.Info("Signed in to mod.io");
        Changed?.Invoke();
    }

    /// <summary>Raised when mod.io refused the stored token (expired or revoked); <see cref="Changed"/> follows.</summary>
    public event Action? Expired;

    /// <summary>Drops a token mod.io no longer accepts.</summary>
    public void Expire()
    {
        if (!IsSignedIn) return;
        Log.Warning("mod.io refused the stored token, signing out");
        Expired?.Invoke();
        SignOut();
    }

    /// <summary>Runs a call with the stored token; a refused token signs the user out (<see cref="Expired"/>).</summary>
    public async Task<T> RunAsync<T>(Func<ModioCredentials, Task<T>> call)
    {
        if (Credentials is not { } auth) throw new InvalidOperationException("Not signed in to mod.io.");
        try
        {
            return await call(auth);
        }
        catch (ModioApiException ex) when (ex.IsAuthError)
        {
            Expire();
            throw;
        }
    }

    /// <summary>Forgets the token here. It stays valid on mod.io until revoked on mod.io/me/access.</summary>
    public void SignOut()
    {
        Credentials = null;
        UserId = 0;
        Username = null;
        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to delete mod.io credentials: {ex.Message}");
        }
        Log.Info("Signed out of mod.io");
        Changed?.Invoke();
    }

    private void Apply(ModioCredentials credentials, long userId, string? username)
    {
        Credentials = credentials;
        UserId = userId;
        Username = username;

        LogService.Instance.RegisterSensitiveValue(credentials.Token, "modioToken");
        // The API host embeds the user id (u-{id}.modapi.io).
        LogService.Instance.RegisterSensitiveValue(credentials.BaseUrl.Host, "modioApiHost");
        // Too short a name would mask unrelated words.
        if (username is { Length: >= 4 }) LogService.Instance.RegisterSensitiveValue(username, "modioUser");
    }

    private void Restore()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var stored = JsonSerializer.Deserialize(ProtectedFile.Read(_filePath, Entropy), ModioAccountJsonContext.Default.StoredModioAccount);
            if (stored is null || !ModioCredentials.TryParseBaseUrl(stored.BaseUrl, out var baseUrl)) return;
            Apply(new ModioCredentials(baseUrl, stored.Token), stored.UserId, stored.Username);
        }
        catch (Exception ex)
        {
            // Unreadable (other user, copied from another machine, corrupted): treat as signed out.
            Log.Warning($"Could not read stored mod.io credentials: {ex.GetType().Name}");
        }
    }

    private void Save()
    {
        if (Credentials is null) return;
        try
        {
            var stored = new StoredModioAccount(Credentials.BaseUrl.ToString(), Credentials.Token, UserId, Username);
            ProtectedFile.Write(_filePath, JsonSerializer.SerializeToUtf8Bytes(stored, ModioAccountJsonContext.Default.StoredModioAccount), Entropy);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save mod.io credentials: {ex.Message}");
        }
    }
}
