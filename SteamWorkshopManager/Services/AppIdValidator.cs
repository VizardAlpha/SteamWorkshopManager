using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Validates Steam AppIds and retrieves game information from the Workshop page.
/// </summary>
public partial class AppIdValidator
{
    private static readonly Logger Log = LogService.GetLogger<AppIdValidator>();
    private readonly HttpClient _httpClient;

    public AppIdValidator(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SteamWorkshopManager/1.0");
    }

    /// <summary>
    /// Validates an AppId by checking if its Workshop page exists.
    /// </summary>
    public async Task<AppIdValidationResult> ValidateAsync(uint appId)
    {
        Log.Debug($"Validating AppId: {appId}");

        try
        {
            var url = $"https://steamcommunity.com/app/{appId}/workshop/";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning($"AppId {appId} validation failed: HTTP {response.StatusCode}");
                return new AppIdValidationResult
                {
                    IsValid = false,
                    ErrorKey = "InvalidAppId"
                };
            }

            var html = await response.Content.ReadAsStringAsync();

            // Check if it's actually a workshop page (not a redirect or error page)
            if (!html.Contains("workshopBrowseItems") &&
                !html.Contains("browseTitle") &&
                !html.Contains("Parcourir par tag") &&
                !html.Contains("Browse by tag"))
            {
                Log.Warning($"AppId {appId} has no Workshop");
                return new AppIdValidationResult
                {
                    IsValid = false,
                    ErrorKey = "NoWorkshop"
                };
            }

            // Extract game name from title: <title>Steam Workshop :: GameName</title>
            var gameName = ExtractGameName(html);

            Log.Info($"AppId {appId} validated: {gameName ?? "Unknown"}");

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

    private static string? ExtractGameName(string html)
    {
        // Primary: <div class="apphub_AppName ellipsis">GameName</div>
        var match = AppHubNameRegex().Match(html);
        if (match.Success)
        {
            var name = match.Groups[1].Value.Trim();
            Log.Debug($"Extracted game name from apphub_AppName: {name}");
            return name;
        }

        // Fallback: <title>Steam Workshop :: GameName</title>
        match = TitleRegexEn().Match(html);
        if (match.Success)
        {
            var name = match.Groups[1].Value.Trim();
            Log.Debug($"Extracted game name from title (EN): {name}");
            return name;
        }

        // Fallback: French title
        match = TitleRegexFr().Match(html);
        if (match.Success)
        {
            var name = match.Groups[1].Value.Trim();
            Log.Debug($"Extracted game name from title (FR): {name}");
            return name;
        }

        Log.Warning("Could not extract game name from HTML");
        return null;
    }

    [GeneratedRegex(@"<div\s+class=""apphub_AppName[^""]*"">([^<]+)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex AppHubNameRegex();

    [GeneratedRegex(@"<title>Steam Workshop\s*::\s*(.+?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegexEn();

    [GeneratedRegex(@"<title>Atelier Steam\s*::\s*(.+?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegexFr();
}

/// <summary>
/// Result of an AppId validation.
/// </summary>
public record AppIdValidationResult
{
    /// <summary>
    /// Whether the AppId is valid and has a Workshop.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// The validated AppId.
    /// </summary>
    public uint AppId { get; init; }

    /// <summary>
    /// The game name extracted from the Workshop page.
    /// </summary>
    public string? GameName { get; init; }

    /// <summary>
    /// Localization key for the error message (if invalid).
    /// </summary>
    public string? ErrorKey { get; init; }

    /// <summary>
    /// Additional error details.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
