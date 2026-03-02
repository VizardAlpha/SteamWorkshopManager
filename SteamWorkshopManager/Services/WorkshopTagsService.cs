using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Result of a tags fetch operation.
/// </summary>
public record TagsResult(
    Dictionary<string, List<string>> TagsByCategory,
    List<string> DropdownCategories);

/// <summary>
/// Cache entry for workshop tags.
/// </summary>
public class TagsCacheEntry
{
    public Dictionary<string, List<string>> TagsByCategory { get; set; } = new();
    public List<string> DropdownCategories { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}

[JsonSerializable(typeof(TagsCacheEntry))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class TagsCacheJsonContext : JsonSerializerContext;

/// <summary>
/// Service for fetching and caching Workshop tags by category.
/// </summary>
public class WorkshopTagsService
{
    private static readonly Logger Log = LogService.GetLogger<WorkshopTagsService>();

    private static readonly string CacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager",
        "cache",
        "tags"
    );

    private readonly HttpClient _httpClient;
    private readonly int _cacheExpirationDays;

    public WorkshopTagsService(HttpClient? httpClient = null, int cacheExpirationDays = 7)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SteamWorkshopManager/1.0");
        _cacheExpirationDays = cacheExpirationDays;
        Directory.CreateDirectory(CacheFolder);
    }

    /// <summary>
    /// Gets tags for an app, using cache if available.
    /// </summary>
    public async Task<TagsResult> GetTagsForAppAsync(uint appId, bool forceRefresh = false)
    {
        var cacheFile = GetCacheFilePath(appId);

        // Check cache first
        if (!forceRefresh && File.Exists(cacheFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(cacheFile);
                var cached = JsonSerializer.Deserialize(json, TagsCacheJsonContext.Default.TagsCacheEntry);

                if (cached != null && cached.UpdatedAt > DateTime.UtcNow.AddDays(-_cacheExpirationDays))
                {
                    Log.Debug($"Using cached tags for AppId {appId} (updated {cached.UpdatedAt})");
                    return new TagsResult(cached.TagsByCategory, cached.DropdownCategories);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to read tags cache for AppId {appId}: {ex.Message}");
            }
        }

        // Fetch from Steam
        Log.Info($"Fetching tags from Steam Workshop for AppId {appId}");
        var result = await FetchTagsFromSteamAsync(appId);

        // Save to cache
        await SaveToCacheAsync(appId, result);

        return result;
    }

    /// <summary>
    /// Fetches tags directly from Steam Workshop page.
    /// </summary>
    private async Task<TagsResult> FetchTagsFromSteamAsync(uint appId)
    {
        var result = new Dictionary<string, List<string>>();
        var dropdownCategories = new List<string>();

        try
        {
            var url = $"https://steamcommunity.com/app/{appId}/workshop/";
            var html = await _httpClient.GetStringAsync(url);

            // Parse HTML with AngleSharp
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(req => req.Content(html));

            // Parse tags from div.panel: supports both categorized (div.title + div.filterOption)
            // and flat (div.filterOption only, no title headers) structures
            var panel = document.QuerySelector("div.panel");
            if (panel != null)
            {
                const string defaultCategory = "Tags";
                string? currentCategory = null;

                foreach (var element in panel.Children)
                {
                    if (element.ClassList.Contains("title"))
                    {
                        currentCategory = element.TextContent.Trim();
                        if (!string.IsNullOrEmpty(currentCategory) && !result.ContainsKey(currentCategory))
                            result[currentCategory] = new List<string>();
                    }
                    else if (element.ClassList.Contains("filterOption"))
                    {
                        // Use current category if set, otherwise default to "Tags"
                        var category = currentCategory ?? defaultCategory;
                        if (!result.ContainsKey(category))
                            result[category] = new List<string>();

                        var input = element.QuerySelector("input.inputTagsFilter");
                        if (input != null)
                        {
                            var tagValue = input.GetAttribute("value");
                            if (!string.IsNullOrEmpty(tagValue))
                            {
                                tagValue = Uri.UnescapeDataString(tagValue.Replace("+", " "));
                                if (!result[category].Contains(tagValue))
                                    result[category].Add(tagValue);
                            }
                        }
                    }

                    // Handle dropdown selects (e.g. "Type" on Space Engineers)
                    var select = element.LocalName == "select"
                        ? element
                        : element.QuerySelector("select");
                    if (select != null)
                    {
                        var category = currentCategory ?? defaultCategory;
                        if (!result.ContainsKey(category))
                            result[category] = new List<string>();

                        if (!dropdownCategories.Contains(category))
                            dropdownCategories.Add(category);

                        foreach (var option in select.QuerySelectorAll("option"))
                        {
                            var value = option.GetAttribute("value");
                            if (string.IsNullOrEmpty(value) || value == "-1") continue;

                            value = Uri.UnescapeDataString(value.Replace("+", " "));
                            if (!result[category].Contains(value))
                                result[category].Add(value);
                        }
                    }
                }
            }

            Log.Info($"Found {result.Count} categories with {result.Values.Sum(v => v.Count)} tags for AppId {appId}");
            foreach (var category in result)
            {
                Log.Debug($"  {category.Key}: {category.Value.Count} tags");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch tags for AppId {appId}", ex);
        }

        return new TagsResult(result, dropdownCategories);
    }

    /// <summary>
    /// Saves tags to cache.
    /// </summary>
    private async Task SaveToCacheAsync(uint appId, TagsResult tagsResult)
    {
        try
        {
            var entry = new TagsCacheEntry
            {
                TagsByCategory = tagsResult.TagsByCategory,
                DropdownCategories = tagsResult.DropdownCategories,
                UpdatedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(entry, TagsCacheJsonContext.Default.TagsCacheEntry);
            var cacheFile = GetCacheFilePath(appId);
            await File.WriteAllTextAsync(cacheFile, json);

            Log.Debug($"Tags cached for AppId {appId}");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to cache tags for AppId {appId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the cache for a specific app.
    /// </summary>
    public void ClearCache(uint appId)
    {
        var cacheFile = GetCacheFilePath(appId);
        if (File.Exists(cacheFile))
        {
            File.Delete(cacheFile);
            Log.Debug($"Cache cleared for AppId {appId}");
        }
    }

    /// <summary>
    /// Gets the last update time for cached tags.
    /// </summary>
    public DateTime? GetCacheUpdateTime(uint appId)
    {
        var cacheFile = GetCacheFilePath(appId);
        if (!File.Exists(cacheFile)) return null;

        try
        {
            var json = File.ReadAllText(cacheFile);
            var cached = JsonSerializer.Deserialize(json, TagsCacheJsonContext.Default.TagsCacheEntry);
            return cached?.UpdatedAt;
        }
        catch
        {
            return null;
        }
    }

    private static string GetCacheFilePath(uint appId)
    {
        return Path.Combine(CacheFolder, $"{appId}.json");
    }
}
