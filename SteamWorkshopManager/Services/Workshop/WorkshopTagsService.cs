using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Services.Workshop;

/// <summary>
/// Public tag payload surfaced to view-models. <see cref="TagsByCategory"/>
/// preserves insertion order (the order Steam lists its categories in), and
/// <see cref="DropdownCategories"/> lists the subset that should render as a
/// single-select combobox instead of a multi-select checkbox list.
/// </summary>
public record TagsResult(
    Dictionary<string, List<string>> TagsByCategory,
    List<string> DropdownCategories);

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
/// Fetches Workshop tag groups for an AppId by scraping the new React-rendered
/// Workshop browse page. Steam still server-renders a data blob
/// (<c>window.SSR.loaderData</c>) containing <c>declaredTags</c>, so we can
/// parse it without a headless browser.
///
/// Results are cached on disk at
/// <c>%AppData%/SteamWorkshopManager/cache/tags/{appId}.json</c> with a 7-day
/// TTL by default.
/// </summary>
public class WorkshopTagsService
{
    private static readonly Logger Log = LogService.GetLogger<WorkshopTagsService>();

    private static readonly string CacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager",
        "cache",
        "tags");

    // Matches `window.SSR.loaderData = [...]` up to the first `];` terminator.
    // The loader entries are double-encoded JSON strings; their contents never
    // close with a bare `];` sequence so the lazy match is safe in practice.
    private static readonly Regex LoaderDataRegex = new(
        @"window\.SSR\.loaderData\s*=\s*(\[[\s\S]*?\])\s*;",
        RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly int _cacheExpirationDays;

    public WorkshopTagsService(HttpClient? httpClient = null, int cacheExpirationDays = 7)
    {
        _httpClient = httpClient ?? SteamHttpClientFactory.Create();
        _cacheExpirationDays = cacheExpirationDays;
        Directory.CreateDirectory(CacheFolder);
    }

    /// <summary>
    /// Returns the tag catalog for <paramref name="appId"/>, hitting the cache
    /// first. On cache miss (or <paramref name="forceRefresh"/>), fetches from
    /// Steam and re-caches. Falls back to a stale cache if the fetch fails.
    /// </summary>
    public async Task<TagsResult> GetTagsForAppAsync(uint appId, bool forceRefresh = false)
    {
        var cacheFile = GetCacheFilePath(appId);

        if (!forceRefresh && TryReadCache(cacheFile, ignoreTtl: false, out var fresh))
        {
            Log.Debug($"Using cached tags for AppId {appId}");
            return fresh;
        }

        try
        {
            var result = await FetchTagsFromSteamAsync(appId);
            if (result.TagsByCategory.Count > 0)
            {
                await SaveToCacheAsync(appId, result);
                return result;
            }
            Log.Warning($"Fetched tag catalog for AppId {appId} is empty; keeping any existing cache.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch tags for AppId {appId}: {ex.Message}", ex);
        }

        // Fetch failed or returned nothing — fall back to whatever we have on
        // disk, even if expired.
        if (TryReadCache(cacheFile, ignoreTtl: true, out var stale))
        {
            Log.Info($"Using stale tag cache for AppId {appId}");
            return stale;
        }

        return new TagsResult(new Dictionary<string, List<string>>(), []);
    }

    /// <summary>
    /// Hits the Workshop browse page and extracts tag groups from the SSR
    /// payload. Prefers <c>readytouse_tags</c> (the set shown when publishing)
    /// and falls back to <c>mtx_tags</c> when the former is absent.
    /// </summary>
    private async Task<TagsResult> FetchTagsFromSteamAsync(uint appId)
    {
        Log.Info($"Fetching tags from Steam Workshop for AppId {appId}");

        var url = $"https://steamcommunity.com/app/{appId}/workshop/";
        var html = await _httpClient.GetStringAsync(url);

        var match = LoaderDataRegex.Match(html);
        if (!match.Success)
        {
            Log.Warning($"Steam page structure changed for AppId {appId}: window.SSR.loaderData not found.");
            return new TagsResult(new Dictionary<string, List<string>>(), []);
        }

        // loaderData is an array of JSON-encoded strings; decode one level
        // first, then parse each entry as its own JSON document.
        var loaderEntries = JsonSerializer.Deserialize(
            match.Groups[1].Value,
            SteamWorkshopJsonContext.Default.ListString) ?? [];

        var declared = FindDeclaredTags(loaderEntries);
        if (declared is null)
        {
            Log.Warning($"No declaredTags found in SSR payload for AppId {appId}.");
            return new TagsResult(new Dictionary<string, List<string>>(), []);
        }

        var groups = declared.ReadyToUseTags?.Count > 0
            ? declared.ReadyToUseTags
            : declared.MtxTags ?? [];

        var categories = new Dictionary<string, List<string>>();
        var dropdowns = new List<string>();

        foreach (var group in groups)
        {
            // Steam occasionally ships untitled orphan groups; bucket them under
            // a generic label so the UI still has a home for the tags.
            var categoryName = string.IsNullOrWhiteSpace(group.Name) ? "Tags" : group.Name;
            if (!categories.ContainsKey(categoryName))
                categories[categoryName] = [];

            foreach (var tag in group.Tags)
            {
                if (tag.AdminOnly) continue;
                var display = !string.IsNullOrWhiteSpace(tag.DisplayName) ? tag.DisplayName : tag.Name;
                if (string.IsNullOrWhiteSpace(display)) continue;
                if (!categories[categoryName].Contains(display))
                    categories[categoryName].Add(display);
            }

            // htmlelement == "select" → render as a single-select dropdown.
            if (string.Equals(group.HtmlElement, "select", StringComparison.OrdinalIgnoreCase)
                && !dropdowns.Contains(categoryName))
            {
                dropdowns.Add(categoryName);
            }
        }

        Log.Info($"Parsed {categories.Count} categories with {categories.Values.Sum(v => v.Count)} tags for AppId {appId}");
        return new TagsResult(categories, dropdowns);
    }

    /// <summary>
    /// Scans the loader-data array for the first entry that carries a
    /// <c>declaredTags</c> node. Entries that fail to parse as JSON are skipped
    /// silently — Steam occasionally inlines non-JSON payloads there.
    /// </summary>
    private static SteamDeclaredTags? FindDeclaredTags(List<string> loaderEntries)
    {
        foreach (var entry in loaderEntries)
        {
            if (string.IsNullOrEmpty(entry) || !entry.Contains("\"declaredTags\"")) continue;

            try
            {
                var parsed = JsonSerializer.Deserialize(entry, SteamWorkshopJsonContext.Default.SteamLoaderEntry);
                if (parsed?.DeclaredTags is not null) return parsed.DeclaredTags;
            }
            catch (JsonException)
            {
                // Non-JSON or schema drift — try the next entry.
            }
        }
        return null;
    }

    private bool TryReadCache(string path, bool ignoreTtl, out TagsResult result)
    {
        result = new TagsResult(new Dictionary<string, List<string>>(), []);
        if (!File.Exists(path)) return false;

        try
        {
            var json = File.ReadAllText(path);
            var cached = JsonSerializer.Deserialize(json, TagsCacheJsonContext.Default.TagsCacheEntry);
            if (cached is null) return false;

            // Cached entries have no TTL in "ignore" mode — lets us surface a
            // stale snapshot as a last-ditch fallback when Steam is unreachable.
            return ignoreTtl
                ? TryMaterialize(cached, out result)
                : IsFresh(cached) && TryMaterialize(cached, out result);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to read tags cache at {path}: {ex.Message}");
            return false;
        }
    }

    private bool IsFresh(TagsCacheEntry entry) =>
        entry.UpdatedAt > DateTime.UtcNow.AddDays(-_cacheExpirationDays);

    private static bool TryMaterialize(TagsCacheEntry entry, out TagsResult result)
    {
        result = new TagsResult(entry.TagsByCategory, entry.DropdownCategories);
        return entry.TagsByCategory.Count > 0;
    }

    private static async Task SaveToCacheAsync(uint appId, TagsResult tagsResult)
    {
        try
        {
            var entry = new TagsCacheEntry
            {
                TagsByCategory = tagsResult.TagsByCategory,
                DropdownCategories = tagsResult.DropdownCategories,
                UpdatedAt = DateTime.UtcNow,
            };

            var json = JsonSerializer.Serialize(entry, TagsCacheJsonContext.Default.TagsCacheEntry);
            await File.WriteAllTextAsync(GetCacheFilePath(appId), json);
            Log.Debug($"Tags cached for AppId {appId}");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to cache tags for AppId {appId}: {ex.Message}");
        }
    }

    public void ClearCache(uint appId)
    {
        var cacheFile = GetCacheFilePath(appId);
        if (!File.Exists(cacheFile)) return;

        try
        {
            File.Delete(cacheFile);
            Log.Debug($"Cache cleared for AppId {appId}");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to clear tag cache for AppId {appId}: {ex.Message}");
        }
    }

    public DateTime? GetCacheUpdateTime(uint appId)
    {
        var cacheFile = GetCacheFilePath(appId);
        if (!File.Exists(cacheFile)) return null;

        try
        {
            var json = File.ReadAllText(cacheFile);
            return JsonSerializer.Deserialize(json, TagsCacheJsonContext.Default.TagsCacheEntry)?.UpdatedAt;
        }
        catch
        {
            return null;
        }
    }

    private static string GetCacheFilePath(uint appId) =>
        Path.Combine(CacheFolder, $"{appId}.json");
}

// ─── DTOs mirroring the Steam SSR payload ────────────────────────────────────

/// <summary>
/// One entry of <c>window.SSR.loaderData</c>. Steam puts many of its React
/// route bundles through this array; we only care about the one that carries
/// <c>declaredTags</c>.
/// </summary>
internal sealed record SteamLoaderEntry(
    [property: JsonPropertyName("declaredTags")] SteamDeclaredTags? DeclaredTags);

/// <summary>
/// Container for the four tag-collection variants Steam exposes. For the
/// Workshop publish flow, <c>readytouse_tags</c> is the authoritative set;
/// <c>mtx_tags</c> holds the same data in a slightly different context and
/// is kept as a fallback.
/// </summary>
internal sealed record SteamDeclaredTags(
    [property: JsonPropertyName("mtx_tags")] List<SteamTagGroup>? MtxTags,
    [property: JsonPropertyName("readytouse_tags")] List<SteamTagGroup>? ReadyToUseTags);

internal sealed record SteamTagGroup(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("htmlelement")] string? HtmlElement,
    [property: JsonPropertyName("tags")] List<SteamTagEntry> Tags);

internal sealed record SteamTagEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("admin_only")] bool AdminOnly);

[JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "ListString")]
[JsonSerializable(typeof(SteamLoaderEntry))]
internal partial class SteamWorkshopJsonContext : JsonSerializerContext;
