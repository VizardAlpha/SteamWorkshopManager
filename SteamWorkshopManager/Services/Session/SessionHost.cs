using System;
using System.Threading.Tasks;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Steam.Worker.Client;
using SteamWorkshopManager.Services.Steam.Worker.Contracts;

namespace SteamWorkshopManager.Services.Session;

/// <summary>
/// Owns the Steam worker process for the currently active session.
///
/// Exactly one worker runs at a time. <see cref="StartSessionAsync"/> spawns a
/// worker bound to the given AppId and calls <c>InitializeAsync</c> on it so
/// Steamworks is live in that child process. Switching sessions disposes the
/// current worker and starts a new one — the shell UI never restarts.
///
/// <see cref="Worker"/> is the typed RPC proxy consumed by
/// <c>WorkerSteamService</c>. <see cref="LastInitResult"/> and
/// <see cref="CurrentUserId"/> cache the outcome of the last init call so the
/// sync <c>ISteamService.Initialize</c> surface can respond without another
/// RPC round-trip.
///
/// Unexpected worker exits (crashes) are caught via
/// <see cref="SteamWorkerClient.UnexpectedExit"/>. The host then respawns with
/// a backoff (1s → 2s → 5s) capped at <see cref="MaxRestartsPerWindow"/>
/// attempts over <see cref="RestartWindow"/> — beyond that we stop trying so a
/// broken Steam install doesn't burn CPU in a restart loop.
/// </summary>
public sealed class SessionHost : IAsyncDisposable
{
    private static readonly Logger Log = LogService.GetLogger<SessionHost>();

    private const int MaxRestartsPerWindow = 3;
    private static readonly TimeSpan RestartWindow = TimeSpan.FromSeconds(60);

    private readonly object _recoveryLock = new();
    private int _restartCount;
    private DateTime _firstRestartAt;

    private SteamWorkerClient? _client;

    public ISteamWorker? Worker => _client?.Proxy;

    public uint ActiveAppId { get; private set; }

    public SteamInitResult LastInitResult { get; private set; } = SteamInitResult.SteamNotRunning;

    public ulong CurrentUserId { get; private set; }

    /// <summary>
    /// Raised on a successful automatic respawn after a crash. Consumers
    /// (<c>MainViewModel</c>) refresh UI state (items, connection dot, etc.).
    /// </summary>
    public event Action? WorkerRecovered;

    /// <summary>
    /// Raised when automatic recovery gives up because the worker keeps
    /// crashing inside the backoff window. The UI should surface a visible
    /// disconnected state and let the user trigger a manual session switch.
    /// </summary>
    public event Action? WorkerUnrecoverable;

    public async Task<SteamInitResult> StartSessionAsync(uint appId)
    {
        if (_client is not null && ActiveAppId == appId)
            return LastInitResult;

        await StopAsync();

        var client = new SteamWorkerClient();
        client.UnexpectedExit += () => _ = HandleCrashAsync(appId);

        try
        {
            await client.StartAsync(appId);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to spawn Steam worker for AppId {appId}: {ex.Message}");
            try { await client.DisposeAsync(); } catch { }
            LastInitResult = SteamInitResult.SteamNotRunning;
            return LastInitResult;
        }

        _client = client;
        ActiveAppId = appId;

        try
        {
            LastInitResult = await client.Proxy.InitializeAsync();
            CurrentUserId = LastInitResult == SteamInitResult.Success
                ? await client.Proxy.GetCurrentUserIdAsync()
                : 0;
            Log.Info($"Session worker ready for AppId {appId}: {LastInitResult}");
        }
        catch (Exception ex)
        {
            Log.Error($"Worker initialization RPC failed for AppId {appId}: {ex.Message}");
            LastInitResult = SteamInitResult.SteamNotRunning;
            CurrentUserId = 0;
        }

        return LastInitResult;
    }

    public async Task StopAsync()
    {
        if (_client is null) return;

        // Flag the shutdown before tearing down so the Exited watcher stays
        // silent — otherwise each session switch would look like a crash.
        _client.MarkIntentionalShutdown();

        try { await _client.Proxy.ShutdownAsync(); } catch { }
        try { await _client.DisposeAsync(); } catch { }

        _client = null;
        ActiveAppId = 0;
        LastInitResult = SteamInitResult.SteamNotRunning;
        CurrentUserId = 0;
    }

    /// <summary>
    /// Handles an unexpected worker exit: waits a backoff delay then tries to
    /// respawn for the same AppId. Aborts if the user swapped sessions during
    /// the delay or if the restart budget is exhausted.
    /// </summary>
    private async Task HandleCrashAsync(uint crashedAppId)
    {
        // Ignore late events from a client that has already been torn down.
        if (ActiveAppId != crashedAppId) return;

        int attempt;
        int delaySeconds;
        lock (_recoveryLock)
        {
            var now = DateTime.UtcNow;
            if (now - _firstRestartAt > RestartWindow)
            {
                _restartCount = 0;
                _firstRestartAt = now;
            }

            _restartCount++;
            if (_restartCount > MaxRestartsPerWindow)
            {
                Log.Error($"Worker crashed {_restartCount} times in the last {RestartWindow.TotalSeconds}s — giving up recovery for AppId {crashedAppId}.");
                attempt = -1;
                delaySeconds = 0;
            }
            else
            {
                attempt = _restartCount;
                delaySeconds = _restartCount switch { 1 => 1, 2 => 2, _ => 5 };
            }
        }

        if (attempt < 0)
        {
            // Drop the dead client so the UI reflects "disconnected" state.
            _client = null;
            LastInitResult = SteamInitResult.SteamNotRunning;
            CurrentUserId = 0;
            try { WorkerUnrecoverable?.Invoke(); } catch { }
            return;
        }

        Log.Warning($"Steam worker crashed for AppId {crashedAppId} — respawn attempt {attempt}/{MaxRestartsPerWindow} in {delaySeconds}s.");

        // Tear down the broken client so StartSessionAsync's early-return
        // guard (`_client != null && ActiveAppId == appId`) doesn't kick in.
        var broken = _client;
        _client = null;
        LastInitResult = SteamInitResult.SteamNotRunning;
        CurrentUserId = 0;
        if (broken is not null)
        {
            try { await broken.DisposeAsync(); } catch { }
        }

        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

        // Session may have been switched or stopped during the backoff.
        if (ActiveAppId != crashedAppId || ActiveAppId == 0)
        {
            Log.Debug($"Session no longer on AppId {crashedAppId} — aborting respawn.");
            return;
        }

        // Reset ActiveAppId so StartSessionAsync does a full spawn (its guard
        // would otherwise skip because ActiveAppId still matches crashedAppId).
        ActiveAppId = 0;

        try
        {
            var result = await StartSessionAsync(crashedAppId);
            if (result == SteamInitResult.Success)
            {
                Log.Info($"Worker recovered for AppId {crashedAppId}");
                try { WorkerRecovered?.Invoke(); } catch { }
            }
            else
            {
                Log.Warning($"Worker respawn for AppId {crashedAppId} initialized with {result}; leaving for user to retry.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Worker recovery failed for AppId {crashedAppId}: {ex.Message}");
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}