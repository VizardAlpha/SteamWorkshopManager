using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Core.Steam;

/// <summary>
/// Fetches public Steam app metadata (icon hashes and similar) via SteamKit2's
/// anonymous PICS (Product Information and Changes Service) access.
///
/// PICS exposes the same data Steam uses in its library UI — importantly, the
/// opaque icon hash needed to build the real library-icon CDN URL:
/// <c>https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{hash}.jpg</c>.
/// The hash is not derivable from the AppId, hence the round-trip through
/// SteamKit2.
///
/// Each call opens a short-lived anonymous session to Steam's connection
/// managers, batches all requested AppIds in a single PICS query, and tears
/// the session down. Callers should batch aggressively (e.g. all known
/// sessions at app startup) rather than calling per-AppId.
/// </summary>
public sealed class SteamAppMetadataService
{
    private static readonly Logger Log = LogService.GetLogger<SteamAppMetadataService>();

    /// <summary>
    /// Resolves the library icon URL for each of the given AppIds. The returned
    /// dictionary only contains entries for which Steam published an icon hash;
    /// AppIds with no metadata (deleted games, DLC-only, unreachable Steam CMs)
    /// are silently omitted so callers can fall back gracefully.
    /// </summary>
    public async Task<Dictionary<uint, string>> GetIconUrlsAsync(
        IEnumerable<uint> appIds,
        CancellationToken ct = default)
    {
        var ids = appIds.Where(id => id > 0).Distinct().ToList();
        var icons = new Dictionary<uint, string>();
        if (ids.Count == 0) return icons;

        var client = new SteamClient();
        var manager = new CallbackManager(client);
        var user = client.GetHandler<SteamUser>()!;
        var apps = client.GetHandler<SteamApps>()!;

        var loggedOnTcs = new TaskCompletionSource<EResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subs = new DisposableBag(
            manager.Subscribe<SteamClient.ConnectedCallback>(_ => user.LogOnAnonymous()),
            manager.Subscribe<SteamUser.LoggedOnCallback>(cb => loggedOnTcs.TrySetResult(cb.Result)),
            manager.Subscribe<SteamClient.DisconnectedCallback>(_ => disconnectedTcs.TrySetResult())
        );

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pumpTask = Task.Run(() =>
        {
            while (!pumpCts.IsCancellationRequested)
                manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
        }, pumpCts.Token);

        try
        {
            client.Connect();

            var logon = await loggedOnTcs.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            if (logon != EResult.OK)
            {
                Log.Debug($"Steam anonymous logon failed: {logon}");
                return icons;
            }

            var requests = ids.Select(id => new SteamApps.PICSRequest(id)).ToList();
            var job = apps.PICSGetProductInfo(requests, Array.Empty<SteamApps.PICSRequest>());
            var resultSet = await job.ToTask().WaitAsync(TimeSpan.FromSeconds(20), ct);

            foreach (var response in resultSet.Results ?? Enumerable.Empty<SteamApps.PICSProductInfoCallback>())
            {
                foreach (var (appId, info) in response.Apps)
                {
                    var hash = info.KeyValues["common"]["icon"].AsString();
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        icons[appId] = BuildIconUrl(appId, hash);
                    }
                }
            }

            Log.Info($"Resolved {icons.Count}/{ids.Count} Steam icon URLs via PICS");
        }
        catch (Exception ex)
        {
            Log.Debug($"PICS icon fetch failed: {ex.Message}");
        }
        finally
        {
            try { user.LogOff(); } catch { }
            try { client.Disconnect(); } catch { }
            try { await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            pumpCts.Cancel();
            try { await pumpTask; } catch { }
        }

        return icons;
    }

    private static string BuildIconUrl(uint appId, string iconHash) =>
        $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{iconHash}.jpg";

    /// <summary>Tiny helper to dispose several callback subscriptions at once.</summary>
    private sealed class DisposableBag(params IDisposable[] items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
            {
                try { item.Dispose(); } catch { }
            }
        }
    }
}
