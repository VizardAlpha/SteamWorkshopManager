using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Steam;

/// <summary>
/// Validates Steam AppIds and retrieves game metadata.
/// Relies exclusively on the Steam Store API (<c>appdetails</c>) so the validator
/// cannot be broken by Workshop page redesigns — the API contract is stable.
/// </summary>
public partial class AppIdValidator
{
    private static readonly Logger Log = LogService.GetLogger<AppIdValidator>();

    /// <summary>
    /// Steam's internal category id for "Steam Workshop" — presence in an app's
    /// <c>categories</c> array is the authoritative signal that the app has a Workshop.
    /// </summary>
    private const int SteamWorkshopCategoryId = 30;

    private readonly HttpClient _httpClient;

    public AppIdValidator(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SteamHttpClientFactory.Create(withAgeGateCookies: true);
    }

    private static readonly Regex StoreUrlAppIdRegex = new(
        @"(?:" +
        @"store\.steampowered\.com/app/" +
        @"|steamcommunity\.com/app/" +
        @"|steamcommunity\.com/sharedfiles/filedetails/\?id=\d+&appid=" +   // rare fallback
        @"|steam(?:community)?://(?:store|rungameid)/" +
        @")(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses a user-supplied AppId from either a raw number or a Steam URL.
    /// Accepts all common variants — store, community workshop, and steam://
    /// protocol links:
    ///   294100
    ///   https://store.steampowered.com/app/294100/
    ///   https://steamcommunity.com/app/2555430/workshop/
    ///   steam://store/294100
    /// Trims whitespace and tolerates trailing slugs or query strings.
    /// </summary>
    public static bool TryParseAppId(string? input, out uint appId)
    {
        appId = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var trimmed = input.Trim();

        if (uint.TryParse(trimmed, out appId) && appId != 0)
            return true;

        var match = StoreUrlAppIdRegex.Match(trimmed);
        if (match.Success && uint.TryParse(match.Groups[1].Value, out appId) && appId != 0)
            return true;

        appId = 0;
        return false;
    }

    /// <summary>
    /// Validates an AppId against the Steam Store API.
    /// Returns a result describing whether the app exists, is a game, and has a Workshop.
    /// </summary>
    public async Task<AppIdValidationResult> ValidateAsync(uint appId)
    {
        Log.Debug($"Validating AppId: {appId}");

        try
        {
            var details = await FetchAppDetailsAsync(appId);

            if (details is null)
            {
                Log.Warning($"AppId {appId} not found on Steam Store");
                return new AppIdValidationResult
                {
                    IsValid = false,
                    ErrorKey = "InvalidAppId",
                };
            }

            if (!details.HasWorkshop)
            {
                Log.Warning($"AppId {appId} ({details.Name}) has no Workshop");
                return new AppIdValidationResult
                {
                    IsValid = false,
                    ErrorKey = "NoWorkshop",
                    GameName = details.Name,
                    AppId = appId,
                };
            }

            Log.Info($"AppId {appId} validated: {details.Name}");
            return new AppIdValidationResult
            {
                IsValid = true,
                GameName = details.Name,
                AppId = appId,
            };
        }
        catch (HttpRequestException ex)
        {
            Log.Error($"Network error validating AppId {appId}: {ex.Message}");
            return new AppIdValidationResult
            {
                IsValid = false,
                ErrorKey = "NetworkError",
                ErrorMessage = ex.Message,
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Error validating AppId {appId}", ex);
            return new AppIdValidationResult
            {
                IsValid = false,
                ErrorKey = "UnknownError",
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Calls Steam Store <c>appdetails</c> and extracts the name + workshop flag in one round-trip.
    /// </summary>
    private async Task<AppDetails?> FetchAppDetailsAsync(uint appId)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
        var json = await _httpClient.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty(appId.ToString(), out var appData)) return null;
        if (!appData.TryGetProperty("success", out var success) || !success.GetBoolean()) return null;
        if (!appData.TryGetProperty("data", out var data)) return null;

        var name = data.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        if (string.IsNullOrEmpty(name)) return null;

        var hasWorkshop = false;
        if (data.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
        {
            foreach (var category in categories.EnumerateArray())
            {
                if (category.TryGetProperty("id", out var idProp) && idProp.GetInt32() == SteamWorkshopCategoryId)
                {
                    hasWorkshop = true;
                    break;
                }
            }
        }

        return new AppDetails(name, hasWorkshop);
    }

    private record AppDetails(string Name, bool HasWorkshop);
}

/// <summary>
/// Result of an AppId validation.
/// </summary>
public record AppIdValidationResult
{
    public bool IsValid { get; init; }
    public uint AppId { get; init; }
    public string? GameName { get; init; }
    public string? ErrorKey { get; init; }
    public string? ErrorMessage { get; init; }
}