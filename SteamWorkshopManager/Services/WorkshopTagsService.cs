using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Cache entry for workshop tags.
/// </summary>
public class TagsCacheEntry
{
    public Dictionary<string, List<string>> TagsByCategory { get; set; } = new();
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
    public async Task<Dictionary<string, List<string>>> GetTagsForAppAsync(uint appId, bool forceRefresh = false)
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
                    return cached.TagsByCategory;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to read tags cache for AppId {appId}: {ex.Message}");
            }
        }

        // Fetch from Steam
        Log.Info($"Fetching tags from Steam Workshop for AppId {appId}");
        var tags = await FetchTagsFromSteamAsync(appId);

        // Save to cache
        await SaveToCacheAsync(appId, tags);

        return tags;
    }

    /// <summary>
    /// Fetches tags directly from Steam Workshop page.
    /// </summary>
    private async Task<Dictionary<string, List<string>>> FetchTagsFromSteamAsync(uint appId)
    {
        var result = new Dictionary<string, List<string>>();

        try
        {
            var url = $"https://steamcommunity.com/app/{appId}/workshop/";
            var html = await _httpClient.GetStringAsync(url);

            // Parse HTML with AngleSharp
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(req => req.Content(html));

            // Find the panel containing tags
            var panel = document.QuerySelector("div.panel");
            if (panel == null)
            {
                Log.Warning($"No tags panel found for AppId {appId}");
                return result;
            }

            string? currentCategory = null;

            // Iterate through all children of the panel
            foreach (var element in panel.Children)
            {
                // Check if it's a category title
                if (element.ClassList.Contains("title"))
                {
                    currentCategory = element.TextContent.Trim();
                    if (!string.IsNullOrEmpty(currentCategory) && !result.ContainsKey(currentCategory))
                    {
                        result[currentCategory] = new List<string>();
                    }
                }
                // Check if it's a tag filter option
                else if (element.ClassList.Contains("filterOption") && currentCategory != null)
                {
                    var input = element.QuerySelector("input.inputTagsFilter");
                    if (input != null)
                    {
                        var tagValue = input.GetAttribute("value");
                        if (!string.IsNullOrEmpty(tagValue))
                        {
                            // Replace + with space (e.g., "Trade+Goods" -> "Trade Goods")
                            tagValue = tagValue.Replace("+", " ");

                            if (!result[currentCategory].Contains(tagValue))
                            {
                                result[currentCategory].Add(tagValue);
                            }
                        }
                    }
                }
            }

            Log.Info($"Found {result.Count} categories with tags for AppId {appId}");
            foreach (var category in result)
            {
                Log.Debug($"  {category.Key}: {category.Value.Count} tags");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch tags for AppId {appId}", ex);
        }

        return result;
    }

    /// <summary>
    /// Saves tags to cache.
    /// </summary>
    private async Task SaveToCacheAsync(uint appId, Dictionary<string, List<string>> tags)
    {
        try
        {
            var entry = new TagsCacheEntry
            {
                TagsByCategory = tags,
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
