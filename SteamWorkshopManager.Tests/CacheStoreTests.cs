using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Services.Cache;

namespace SteamWorkshopManager.Tests;

public sealed record CachedThing(string Name);

[JsonSerializable(typeof(CachedThing))]
internal partial class CacheTestJsonContext : JsonSerializerContext;

[TestClass]
public class CacheStoreTests
{
    private static readonly CacheKey Key = CacheKey.SteamApp(42, "thing.json");
    private static readonly CachePolicy OneHour = new(TimeSpan.FromHours(1));

    private string _root = null!;

    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "swm-cache-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<CacheStore> CreateAsync(bool enabled = true, long? limit = null, string[]? legacy = null)
    {
        var store = new CacheStore(_root, enabled, limit, legacy);
        await store.Initialization;
        return store;
    }

    private static Task<CachedThing?> Fetched(string name) => Task.FromResult<CachedThing?>(new CachedThing(name));

    [TestMethod]
    public async Task SetThenGet_RoundTripsValueAndVersion()
    {
        var store = await CreateAsync();

        await store.SetAsync(Key, new CachedThing("a"), CacheTestJsonContext.Default.CachedThing, version: "v1");
        var entry = await store.GetAsync(Key, CacheTestJsonContext.Default.CachedThing);

        Assert.IsNotNull(entry);
        Assert.AreEqual("a", entry.Value.Name);
        Assert.AreEqual("v1", entry.Version);
        Assert.IsTrue(store.SizeBytes > 0);
        Assert.IsNotNull(store.LastWriteUtc);
    }

    [TestMethod]
    public async Task Disabled_NeitherReadsNorWrites()
    {
        var store = await CreateAsync(enabled: false);

        await store.SetAsync(Key, new CachedThing("a"), CacheTestJsonContext.Default.CachedThing);

        Assert.IsNull(await store.GetAsync(Key, CacheTestJsonContext.Default.CachedThing));
        Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public async Task CorruptEntry_IsDroppedAndMisses()
    {
        var store = await CreateAsync();
        await store.SetFileAsync(Key, "not json"u8.ToArray());

        Assert.IsNull(await store.GetAsync(Key, CacheTestJsonContext.Default.CachedThing));
        Assert.IsNull(store.GetFilePath(Key));
    }

    [TestMethod]
    public async Task LimitReached_PausesGrowingWritesAndNotifiesOnce()
    {
        var store = await CreateAsync(limit: 100);
        var notifications = 0;
        store.LimitReached += () => notifications++;

        var first = CacheKey.SteamApp(1, "a");
        await store.SetFileAsync(first, new byte[80]);
        await store.SetFileAsync(CacheKey.SteamApp(1, "b"), new byte[80]);
        await store.SetFileAsync(CacheKey.SteamApp(1, "c"), new byte[10]);

        Assert.IsTrue(store.IsFull);
        Assert.AreEqual(1, notifications);
        Assert.AreEqual(80, store.SizeBytes);
        Assert.IsNull(store.GetFilePath(CacheKey.SteamApp(1, "b")));

        // Rewrites that don't grow the cache still go through.
        await store.SetFileAsync(first, new byte[60]);
        Assert.AreEqual(60, store.SizeBytes);
        Assert.AreEqual(60, new FileInfo(store.GetFilePath(first)!).Length);
    }

    [TestMethod]
    public async Task Configure_ResumesPausedWrites()
    {
        var store = await CreateAsync(limit: 100);
        await store.SetFileAsync(CacheKey.SteamApp(1, "a"), new byte[150]);
        Assert.IsTrue(store.IsFull);

        store.Configure(enabled: true, limitBytes: null);
        await store.SetFileAsync(CacheKey.SteamApp(1, "a"), new byte[150]);

        Assert.IsFalse(store.IsFull);
        Assert.AreEqual(150, store.SizeBytes);
    }

    [TestMethod]
    public async Task GetOrFetch_ServesFreshEntryWithoutFetching()
    {
        var store = await CreateAsync();
        await store.SetAsync(Key, new CachedThing("cached"), CacheTestJsonContext.Default.CachedThing);
        var fetches = 0;

        var value = await store.GetOrFetchAsync(Key, CacheTestJsonContext.Default.CachedThing,
            () => { fetches++; return Fetched("new"); }, OneHour);

        Assert.AreEqual("cached", value!.Name);
        Assert.AreEqual(0, fetches);
    }

    [TestMethod]
    public async Task GetOrFetch_OtherVersionIsAMiss()
    {
        var store = await CreateAsync();
        await store.GetOrFetchAsync(Key, CacheTestJsonContext.Default.CachedThing, () => Fetched("v1"), OneHour, version: "1");

        var same = await store.GetOrFetchAsync(Key, CacheTestJsonContext.Default.CachedThing, () => Fetched("unused"), OneHour, version: "1");
        var newer = await store.GetOrFetchAsync(Key, CacheTestJsonContext.Default.CachedThing, () => Fetched("v2"), OneHour, version: "2");

        Assert.AreEqual("v1", same!.Name);
        Assert.AreEqual("v2", newer!.Name);
        Assert.AreEqual("2", (await store.GetAsync(Key, CacheTestJsonContext.Default.CachedThing))!.Version);
    }

    [TestMethod]
    public async Task GetOrFetch_RefetchesExpiredEntry()
    {
        var store = await CreateAsync();
        await store.SetAsync(Key, new CachedThing("cached"), CacheTestJsonContext.Default.CachedThing);

        var value = await store.GetOrFetchAsync(Key, CacheTestJsonContext.Default.CachedThing,
            () => Fetched("new"), new CachePolicy(TimeSpan.Zero));

        Assert.AreEqual("new", value!.Name);
        Assert.AreEqual("new", (await store.GetAsync(Key, CacheTestJsonContext.Default.CachedThing))!.Value.Name);
    }

    [TestMethod]
    public async Task GetOrFetch_ServesStaleEntryWhenFetchFails()
    {
        var store = await CreateAsync();
        await store.SetAsync(Key, new CachedThing("stale"), CacheTestJsonContext.Default.CachedThing);

        var value = await store.GetOrFetchAsync<CachedThing>(Key, CacheTestJsonContext.Default.CachedThing,
            () => throw new InvalidOperationException("offline"), new CachePolicy(TimeSpan.Zero), forceRefresh: true);

        Assert.AreEqual("stale", value!.Name);
    }

    [TestMethod]
    public async Task GetOrFetch_ConcurrentCallsShareOneFetch()
    {
        var store = await CreateAsync();
        var gate = new TaskCompletionSource();
        var fetches = 0;

        async Task<CachedThing?> SlowFetch()
        {
            fetches++;
            await gate.Task;
            return new CachedThing("once");
        }

        var calls = Enumerable.Range(0, 5)
            .Select(_ => store.GetOrFetchAsync(Key, CacheTestJsonContext.Default.CachedThing, SlowFetch, OneHour))
            .ToList();
        await Task.Delay(50);
        gate.SetResult();
        var results = await Task.WhenAll(calls);

        Assert.AreEqual(1, fetches);
        Assert.IsTrue(results.All(r => r!.Name == "once"));
    }

    [TestMethod]
    public async Task RemoveScope_DeletesOnlyThatScope()
    {
        var store = await CreateAsync();
        await store.SetFileAsync(CacheKey.SteamApp(1, "a"), new byte[10]);
        await store.SetFileAsync(CacheKey.SteamApp(2, "a"), new byte[20]);

        Assert.AreEqual(10, store.MeasureScope(CacheKey.Steam, CacheKey.AppScope(1)));
        store.RemoveScope(CacheKey.Steam, CacheKey.AppScope(1));

        Assert.IsNull(store.GetFilePath(CacheKey.SteamApp(1, "a")));
        Assert.IsNotNull(store.GetFilePath(CacheKey.SteamApp(2, "a")));
        Assert.AreEqual(20, store.SizeBytes);
    }

    [TestMethod]
    public async Task Clear_EmptiesCacheAndResumesWrites()
    {
        var store = await CreateAsync(limit: 10);
        await store.SetFileAsync(CacheKey.SteamApp(1, "a"), new byte[5]);
        await store.SetFileAsync(CacheKey.SteamApp(1, "b"), new byte[50]);

        await store.ClearAsync();

        Assert.AreEqual(0, store.SizeBytes);
        Assert.IsFalse(store.IsFull);
        Assert.IsNull(store.LastWriteUtc);
    }

    [TestMethod]
    public async Task Startup_DropsObsoleteEntriesAndCountsTheRest()
    {
        var oldFile = Path.Combine(_root, "steam", "app-1", "old");
        var newFile = Path.Combine(_root, "steam", "app-1", "new");
        Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
        await File.WriteAllBytesAsync(oldFile, new byte[30]);
        await File.WriteAllBytesAsync(newFile, new byte[12]);
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - CacheStore.ObsoleteAfter - TimeSpan.FromDays(1));

        var store = await CreateAsync();

        Assert.IsFalse(File.Exists(oldFile));
        Assert.AreEqual(12, store.SizeBytes);
    }

    [TestMethod]
    public async Task Startup_DeletesLegacyFolders()
    {
        var legacy = Path.Combine(_root + "-legacy", "cache");
        Directory.CreateDirectory(legacy);
        await File.WriteAllTextAsync(Path.Combine(legacy, "x.jpg"), "x");

        try
        {
            await CreateAsync(legacy: [legacy]);
            Assert.IsFalse(Directory.Exists(legacy));
        }
        finally
        {
            if (Directory.Exists(_root + "-legacy")) Directory.Delete(_root + "-legacy", recursive: true);
        }
    }

    [TestMethod]
    [DataRow("..")]
    [DataRow("a/../b")]
    [DataRow("UPPER")]
    [DataRow("a\\b")]
    [DataRow("")]
    public void Key_RejectsUnsafeNames(string name)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CacheKey(CacheKey.Steam, "app-1", name));
    }

    [TestMethod]
    public void ImageKey_IsStableAndUrlSpecific()
    {
        Assert.AreEqual(CacheKey.Image("https://a/1.jpg"), CacheKey.Image("https://a/1.jpg"));
        Assert.AreNotEqual(CacheKey.Image("https://a/1.jpg"), CacheKey.Image("https://a/2.jpg"));
    }
}
