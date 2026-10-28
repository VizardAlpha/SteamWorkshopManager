using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Cache;

/// <summary>
/// One file per entry under <see cref="AppPaths.CacheRoot"/>. JSON entries are wrapped in
/// an envelope (fetchedAt, version, data); binary entries are stored as-is. Entries not
/// rewritten for <see cref="ObsoleteAfter"/> are dropped on startup.
/// </summary>
public sealed class CacheStore : ICacheStore
{
    private static readonly Logger Log = LogService.GetLogger<CacheStore>();

    internal static readonly TimeSpan ObsoleteAfter = TimeSpan.FromDays(30);
    private static readonly TimeSpan OrphanTempAge = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<CacheKey, Task> _inFlight = new();

    private bool _enabled;
    private long? _limit;
    private long _size;
    private bool _full;
    private DateTime? _lastWrite;

    public CacheStore(ISettingsService settings)
        : this(AppPaths.CacheRoot, settings.Settings.CacheEnabled, LimitFromMegabytes(settings.Settings.CacheLimitMb),
            AppPaths.LegacyCacheFolders)
    {
    }

    internal CacheStore(string root, bool enabled, long? limitBytes, IReadOnlyList<string>? legacyFolders = null)
    {
        RootPath = root;
        _enabled = enabled;
        _limit = limitBytes;
        Initialization = Task.Run(() =>
        {
            DeleteLegacyFolders(legacyFolders ?? []);
            Scan();
        });
    }

    /// <summary>Startup scan (size, last write, obsolete entries). Exposed for tests.</summary>
    internal Task Initialization { get; }

    public string RootPath { get; }

    public bool IsEnabled { get { lock (_gate) return _enabled; } }
    public long? LimitBytes { get { lock (_gate) return _limit; } }
    public long SizeBytes { get { lock (_gate) return _size; } }
    public bool IsFull { get { lock (_gate) return _full; } }
    public DateTime? LastWriteUtc { get { lock (_gate) return _lastWrite; } }

    public event Action? Changed;
    public event Action? LimitReached;

    /// <summary>0 or less means unlimited.</summary>
    public static long? LimitFromMegabytes(int megabytes) => megabytes > 0 ? megabytes * 1024L * 1024L : null;

    public void Configure(bool enabled, long? limitBytes)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _limit = limitBytes;
            // A new limit resumes writes; the next one that does not fit pauses them again.
            _full = false;
        }
        Changed?.Invoke();
    }

    public async Task<CacheEntry<T>?> GetAsync<T>(CacheKey key, JsonTypeInfo<T> type)
    {
        if (!IsEnabled) return null;

        var path = PathFor(key);
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;

            var value = root.GetProperty("data").Deserialize(type);
            if (value is null) return null;

            var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return new CacheEntry<T>(value, root.GetProperty("fetchedAt").GetDateTime(), version);
        }
        catch (Exception ex)
        {
            // Corrupt or from an older shape: refetching beats failing forever.
            Log.Debug($"Unreadable cache entry {key}: {ex.Message}");
            Remove(key);
            return null;
        }
    }

    public async Task SetAsync<T>(CacheKey key, T value, JsonTypeInfo<T> type, string? version = null)
    {
        if (!IsEnabled) return;

        using var buffer = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("fetchedAt", DateTime.UtcNow);
            if (version is not null) writer.WriteString("version", version);
            writer.WritePropertyName("data");
            JsonSerializer.Serialize(writer, value, type);
            writer.WriteEndObject();
        }

        await WriteAsync(key, buffer.ToArray()).ConfigureAwait(false);
    }

    public async Task<T?> GetOrFetchAsync<T>(CacheKey key, JsonTypeInfo<T> type, Func<Task<T?>> fetch, CachePolicy policy,
        bool forceRefresh = false, string? version = null) where T : class
    {
        var cached = await GetAsync(key, type).ConfigureAwait(false);
        if (!forceRefresh && cached is not null && policy.IsFresh(cached.FetchedAtUtc)
            && (version is null || cached.Version == version))
            return cached.Value;

        var fetched = await FetchOnceAsync(key, fetch).ConfigureAwait(false);
        if (fetched is not null)
        {
            await SetAsync(key, fetched, type, version).ConfigureAwait(false);
            return fetched;
        }

        return policy.ServeStaleOnError ? cached?.Value : null;
    }

    public string? GetFilePath(CacheKey key)
    {
        if (!IsEnabled) return null;
        var path = PathFor(key);
        return File.Exists(path) ? path : null;
    }

    public Task SetFileAsync(CacheKey key, byte[] content) => IsEnabled ? WriteAsync(key, content) : Task.CompletedTask;

    public void Remove(CacheKey key)
    {
        var path = PathFor(key);
        var length = SafeLength(path);
        if (!TryDeleteFile(path)) return;

        lock (_gate) _size = Math.Max(0, _size - length);
        Changed?.Invoke();
    }

    public long MeasureScope(string platform, string scope) => MeasureFolder(ScopePath(platform, scope));

    public void RemoveScope(string platform, string scope)
    {
        var folder = ScopePath(platform, scope);
        var length = MeasureFolder(folder);
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to delete cache scope {platform}/{scope}: {ex.Message}");
            return;
        }

        lock (_gate) _size = Math.Max(0, _size - length);
        Changed?.Invoke();
    }

    public Task ClearAsync() => Task.Run(() =>
    {
        try
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warning($"Cache clear incomplete: {ex.Message}");
        }

        // Files that survived (locked by a reader) still count.
        var remaining = MeasureFolder(RootPath);
        lock (_gate)
        {
            _size = remaining;
            _full = false;
            _lastWrite = null;
        }
        Log.Info("Cache cleared");
        Changed?.Invoke();
    });

    private Task<T?> FetchOnceAsync<T>(CacheKey key, Func<Task<T?>> fetch) where T : class
    {
        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = _inFlight.GetOrAdd(key, tcs.Task);
        if (existing != tcs.Task) return (Task<T?>)existing;

        _ = RunAsync();
        return tcs.Task;

        async Task RunAsync()
        {
            try
            {
                tcs.SetResult(await fetch().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Log.Warning($"Fetch failed for {key}: {ex.Message}");
                tcs.SetResult(null);
            }
            finally
            {
                _inFlight.TryRemove(new KeyValuePair<CacheKey, Task>(key, tcs.Task));
            }
        }
    }

    private async Task WriteAsync(CacheKey key, byte[] content)
    {
        var path = PathFor(key);
        var delta = content.Length - SafeLength(path);
        bool refused, justPaused;

        lock (_gate)
        {
            // Rewrites that don't grow the cache still go through so entries stay current.
            refused = delta > 0 && (_full || (_limit is { } limit && _size + delta > limit));
            justPaused = refused && !_full;
            if (refused) _full = true;
            else _size += delta;
        }

        if (refused)
        {
            if (!justPaused) return;
            Log.Warning($"Cache limit reached ({Formatters.Bytes(LimitBytes ?? 0)}), writes paused");
            LimitReached?.Invoke();
            Changed?.Invoke();
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await AtomicFile.WriteAllBytesAsync(path, content).ConfigureAwait(false);
            lock (_gate) _lastWrite = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            lock (_gate) _size = Math.Max(0, _size - delta);
            Log.Debug($"Cache write failed for {key}: {ex.Message}");
        }
        Changed?.Invoke();
    }

    private void DeleteLegacyFolders(IReadOnlyList<string> folders)
    {
        foreach (var folder in folders)
        {
            if (string.Equals(Path.GetFullPath(folder), Path.GetFullPath(RootPath), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (!Directory.Exists(folder)) continue;
                Directory.Delete(folder, recursive: true);
                Log.Info($"Deleted legacy cache folder {folder}");
            }
            catch (Exception ex)
            {
                Log.Debug($"Legacy cache folder {folder} not deleted: {ex.Message}");
            }
        }
    }

    private void Scan()
    {
        if (!Directory.Exists(RootPath)) return;

        long total = 0;
        DateTime? last = null;
        var now = DateTime.UtcNow;

        try
        {
            foreach (var file in new DirectoryInfo(RootPath).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                var age = now - file.LastWriteTimeUtc;
                var orphanTemp = file.Name.EndsWith(".tmp", StringComparison.Ordinal) && age > OrphanTempAge;
                if (orphanTemp || age > ObsoleteAfter)
                {
                    TryDeleteFile(file.FullName);
                    continue;
                }

                total += file.Length;
                if (last is null || file.LastWriteTimeUtc > last) last = file.LastWriteTimeUtc;
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Cache scan incomplete: {ex.Message}");
        }

        lock (_gate)
        {
            _size += total;
            if (_lastWrite is null || last > _lastWrite) _lastWrite = last;
        }
        Changed?.Invoke();
    }

    private string PathFor(CacheKey key) => Path.Combine(RootPath, key.RelativePath);

    private string ScopePath(string platform, string scope) =>
        Path.GetDirectoryName(PathFor(new CacheKey(platform, scope, "x")))!;

    private static long MeasureFolder(string folder)
    {
        if (!Directory.Exists(folder)) return 0;

        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
                total += file.Length;
        }
        catch
        {
            // Concurrent delete: the total is a best effort.
        }
        return total;
    }

    private static long SafeLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
