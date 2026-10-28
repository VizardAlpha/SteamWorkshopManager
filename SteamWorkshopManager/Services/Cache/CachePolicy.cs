using System;

namespace SteamWorkshopManager.Services.Cache;

/// <summary>A cached value with when it was fetched and the source's version stamp, if any.</summary>
public sealed record CacheEntry<T>(T Value, DateTime FetchedAtUtc, string? Version);

/// <summary>
/// How long an entry is served before a refetch (<c>null</c> = until invalidated), and
/// whether an expired entry is served when the refetch fails.
/// </summary>
public sealed record CachePolicy(TimeSpan? MaxAge, bool ServeStaleOnError = true)
{
    public bool IsFresh(DateTime fetchedAtUtc) => MaxAge is not { } maxAge || DateTime.UtcNow - fetchedAtUtc < maxAge;
}
