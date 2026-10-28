using System;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace SteamWorkshopManager.Services.Cache;

/// <summary>
/// On-disk cache shared by every platform. When disabled, reads miss and writes are
/// dropped. When the size limit is reached, writes that would grow it are paused
/// (nothing is evicted) until the user raises the limit, disables or clears the cache.
/// </summary>
public interface ICacheStore
{
    string RootPath { get; }
    bool IsEnabled { get; }

    /// <summary>Null when unlimited.</summary>
    long? LimitBytes { get; }

    long SizeBytes { get; }

    /// <summary>Writes are paused because the limit was reached.</summary>
    bool IsFull { get; }

    DateTime? LastWriteUtc { get; }

    /// <summary>Size, state or settings changed. Raised from any thread.</summary>
    event Action? Changed;

    /// <summary>Raised once each time writes get paused. Raised from any thread.</summary>
    event Action? LimitReached;

    void Configure(bool enabled, long? limitBytes);

    Task<CacheEntry<T>?> GetAsync<T>(CacheKey key, JsonTypeInfo<T> type);

    Task SetAsync<T>(CacheKey key, T value, JsonTypeInfo<T> type, string? version = null);

    /// <summary>
    /// Serves the entry while <paramref name="policy"/> says it is fresh, fetches otherwise.
    /// <paramref name="fetch"/> returns null on failure. One fetch per key at a time.
    /// </summary>
    Task<T?> GetOrFetchAsync<T>(CacheKey key, JsonTypeInfo<T> type, Func<Task<T?>> fetch, CachePolicy policy,
        bool forceRefresh = false) where T : class;

    /// <summary>Path of a cached binary entry, null on miss.</summary>
    string? GetFilePath(CacheKey key);

    Task SetFileAsync(CacheKey key, byte[] content);

    void Remove(CacheKey key);

    long MeasureScope(string platform, string scope);

    void RemoveScope(string platform, string scope);

    Task ClearAsync();
}
