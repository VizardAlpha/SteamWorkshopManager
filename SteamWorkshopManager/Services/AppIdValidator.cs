using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Validates Steam AppIds and retrieves game information.
/// Uses Steam Store API for game name and Workshop page for workshop existence check.
/// </summary>
public partial class AppIdValidator
{
    private static readonly Logger Log = LogService.GetLogger<AppIdValidator>();
    private readonly HttpClient _httpClient;

    public AppIdValidator(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SteamHttpClientFactory.Create();
    }

    /// <summary>
    /// Validates an AppId by checking if its Workshop page exists
    /// and fetching the game name from the Steam Store API.
    /// </summary>
    public async Task<AppIdValidationResult> ValidateAsync(uint appId)
    {
        Log.Debug($"Validating AppId: {appId}");

        try
        {
            // Fetch game name and workshop check in parallel
            var nameTask = FetchGameNameAsync(appId);
            var workshopTask = CheckWorkshopExistsAsync(appId);

            await Task.WhenAll(nameTask, workshopTask);

            var gameName = nameTask.Result;
            var hasWorkshop = workshopTask.Result;

            if (gameName == null)
            {
                Log.Warning($"AppId {appId} not found on Steam Store");
                return new AppIdValidationResult
                {
                    IsValid = false,
                    ErrorKey = "InvalidAppId"
                };
            }

            if (!hasWorkshop)
            {
                Log.Warning($"AppId {appId} ({gameName}) has no Workshop");
                return new AppIdValidationResult
                {
                    IsValid = false,
                    ErrorKey = "NoWorkshop"
                };
            }

            Log.Info($"AppId {appId} validated: {gameName}");
            return new AppIdValidationResult
            {
                IsValid = true,
                GameName = gameName,
                AppId = appId
            };
        }
        catch (HttpRequestException ex)
        {
            Log.Error($"Network error validating AppId {appId}: {ex.Message}");
            return new AppIdValidationResult
            {
                IsValid = false,
                ErrorKey = "NetworkError",
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Error validating AppId {appId}", ex);
            return new AppIdValidationResult
            {
                IsValid = false,
                ErrorKey = "UnknownError",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Fetches the game name from the Steam Store API (no age gate).
    /// </summary>
    private async Task<string?> FetchGameNameAsync(uint appId)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
            var json = await _httpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty(appId.ToString(), out var appData) &&
                appData.TryGetProperty("success", out var success) && success.GetBoolean() &&
                appData.TryGetProperty("data", out var data) &&
                data.TryGetProperty("name", out var name))
            {
                var gameName = name.GetString();
                Log.Debug($"Game name from Store API: {gameName}");
                return gameName;
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to fetch game name from Store API for {appId}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Checks if the app has a Workshop by looking at the Workshop page.
    /// </summary>
    private async Task<bool> CheckWorkshopExistsAsync(uint appId)
    {
        try
        {
            var url = $"https://steamcommunity.com/app/{appId}/workshop/";
            using var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return false;

            var html = await response.Content.ReadAsStringAsync();

            // Any of these markers indicate a valid Workshop page
            return html.Contains("workshopBrowseItems") ||
                   html.Contains("browseTitle") ||
                   html.Contains("Parcourir par tag") ||
                   html.Contains("Browse by tag") ||
                   html.Contains("age_gate_container") ||
                   html.Contains("mature_content");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to check Workshop existence for {appId}: {ex.Message}");
            return false;
        }
    }
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
