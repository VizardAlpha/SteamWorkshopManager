using System;
using System.Threading;
using System.Threading.Tasks;
using WorkshopManager.Models;
using WorkshopManager.Services.Core;
using WorkshopManager.Services.Log;
using WorkshopManager.Services.Steam;
using WorkshopManager.Services.Steam.Worker.Client;
using WorkshopManager.Services.Steam.Worker.Contracts;

namespace WorkshopManager.Services.Session;

/// <summary>
/// Owns the Steam worker process for the currently active session.
///
/// Exactly one worker runs at a time. <see cref="StartSessionAsync"/> spawns a
/// worker bound to the given AppId and calls <c>InitializeAsync</c> on it so
/// Steamworks is live in that child process. Switching sessions disposes the
/// current worker and starts a new one - the shell UI never restarts.
///
/// <see cref="Worker"/> is the typed RPC proxy consumed by
/// <c>WorkerSteamService</c>. <see cref="LastInitResult"/> and
/// <see cref="CurrentUserId"/> cache the outcome of the last init call so the
/// shell can read the connection state without another RPC round-trip.
/// Start, stop and crash recovery are serialized.
///
/// Unexpected worker exits (crashes) are caught via
/// <see cref="SteamWorkerClient.UnexpectedExit"/>. The host then respawns with
/// a backoff (1s → 2s → 5s) capped at <see cref="MaxRestartsPerWindow"/>
/// attempts over <see cref="RestartWindow"/> - beyond that we stop trying so a
/// broken Steam install doesn't burn CPU in a restart loop.
///
/// Standby (see <see cref="SteamIdleMode"/>): once idle, the worker is stopped on purpose so Steam
/// no longer shows the user as playing. The session stays active, its last init result is kept,
/// and the next call made through <see cref="Worker"/> starts the worker again.
/// </summary>
public sealed class SessionHost : IAsyncDisposable
{
    private static readonly Logger Log = LogService.GetLogger<SessionHost>();

    private const int MaxRestartsPerWindow = 3;
    private static readonly TimeSpan RestartWindow = TimeSpan.FromSeconds(60);

    // Start, stop and crash recovery run one at a time: a switch during a respawn backoff
    // could otherwise spawn two workers.
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _recoveryLock = new();
    private int _restartCount;
    private DateTime _firstRestartAt;

    private SteamWorkerClient? _client;

    private readonly ISettingsService _settings;
    private readonly StandbyWorker _gate;

    // AppId of the session whose worker was stopped on purpose; 0 when not on standby.
    private uint _standbyAppId;
    private int _callsInFlight;
    private readonly object _idleLock = new();
    private CancellationTokenSource? _idleTimer;

    public SessionHost(ISettingsService settings)
    {
        _settings = settings;
        _gate = new StandbyWorker(this);
    }

    /// <summary>Null when no Steam session is active. Calling it wakes a worker on standby.</summary>
    public ISteamWorker? Worker => _client is not null || _standbyAppId != 0 ? _gate : null;

    /// <summary>The worker process itself, null on standby: for calls that must not wake it.</summary>
    internal ISteamWorker? RunningWorker => _client?.Proxy;

    /// <summary>The session is active but its worker is stopped until the next Steam call.</summary>
    public bool IsStandby => _client is null && _standbyAppId != 0;

    /// <summary>Raised from any thread when the worker goes on standby or wakes up.</summary>
    public event Action? ConnectionChanged;

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

    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Starts the worker a Steam session needs; sessions on other platforms run without one.</summary>
    public async Task<SteamInitResult> StartSessionAsync(WorkshopSession session)
    {
        if (session is SteamSession steam) return await StartSessionAsync(steam.AppId);

        await StopAsync();
        return SteamInitResult.SteamNotRunning;
    }

    private async Task<SteamInitResult> StartSessionAsync(uint appId)
    {
        await _lifecycle.WaitAsync();
        try { return await StartCoreAsync(appId); }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _lifecycle.Release(); }
    }

    /// <summary>
    /// Hands out the running worker for one call, starting it first when it is on standby.
    /// Every call must be matched by <see cref="Release"/>.
    /// </summary>
    internal async Task<ISteamWorker> AcquireAsync()
    {
        Interlocked.Increment(ref _callsInFlight);
        CancelIdleTimer();
        try
        {
            var woke = false;
            ISteamWorker worker;
            // Under the lifecycle lock: a standby stop can't slip in between the check and the call.
            await _lifecycle.WaitAsync();
            try
            {
                if (_client is null)
                {
                    var appId = _standbyAppId;
                    if (appId == 0) throw new InvalidOperationException("No Steam session is active.");

                    Log.Info($"Waking the Steam worker for AppId {appId}");
                    var result = await StartCoreAsync(appId);
                    woke = true;
                    if (result != SteamInitResult.Success)
                    {
                        // Steam went away meanwhile: back to standby so the next call tries again.
                        await StopCoreAsync();
                        _standbyAppId = appId;
                        ActiveAppId = appId;
                        LastInitResult = result;
                    }
                }

                worker = _client?.Proxy ?? throw new InvalidOperationException("Steam is not available.");
            }
            finally
            {
                _lifecycle.Release();
                if (woke) RaiseConnectionChanged();
            }

            return worker;
        }
        catch
        {
            Release();
            throw;
        }
    }

    internal void Release()
    {
        if (Interlocked.Decrement(ref _callsInFlight) == 0) ScheduleStandby();
    }

    /// <summary>Applies a change of the idle setting to a worker that is already idle.</summary>
    public void RefreshIdlePolicy()
    {
        CancelIdleTimer();
        if (Volatile.Read(ref _callsInFlight) == 0) ScheduleStandby();
    }

    private void ScheduleStandby()
    {
        var settings = _settings.Settings;
        if (WorkerIdlePolicy.DelayFor(settings.SteamIdleMode, settings.SteamIdleSeconds) is not { } delay) return;

        var timer = new CancellationTokenSource();
        lock (_idleLock)
        {
            _idleTimer?.Cancel();
            _idleTimer = timer;
        }

        _ = EnterStandbyAfterAsync(delay, timer.Token);
    }

    private void CancelIdleTimer()
    {
        lock (_idleLock)
        {
            _idleTimer?.Cancel();
            _idleTimer = null;
        }
    }

    private async Task EnterStandbyAfterAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);

            await _lifecycle.WaitAsync(ct);
            try
            {
                // Only a healthy, idle worker goes on standby: a failed init must stay visible.
                if (ct.IsCancellationRequested || _client is null || Volatile.Read(ref _callsInFlight) > 0
                    || LastInitResult != SteamInitResult.Success)
                    return;

                var appId = ActiveAppId;
                var userId = CurrentUserId;
                await StopCoreAsync();
                _standbyAppId = appId;
                ActiveAppId = appId;
                LastInitResult = SteamInitResult.Success;
                CurrentUserId = userId;
                Log.Info($"Steam worker for AppId {appId} is on standby");
            }
            finally
            {
                _lifecycle.Release();
            }

            RaiseConnectionChanged();
        }
        catch (OperationCanceledException)
        {
            // A new call or a session change came first.
        }
        catch (Exception ex)
        {
            Log.Error($"Could not put the Steam worker on standby: {ex.Message}");
        }
    }

    private void RaiseConnectionChanged()
    {
        try { ConnectionChanged?.Invoke(); }
        catch (Exception ex) { Log.Debug($"ConnectionChanged handler threw: {ex.Message}"); }
    }

    private async Task<SteamInitResult> StartCoreAsync(uint appId)
    {
        if (_client is not null && ActiveAppId == appId)
            return LastInitResult;

        await StopCoreAsync();

        var client = new SteamWorkerClient();
        client.UnexpectedExit += () => _ = HandleCrashAsync(client, appId);

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
            LastInitResult = await client.Proxy.InitializeAsync().WaitAsync(InitTimeout);
            CurrentUserId = LastInitResult == SteamInitResult.Success
                ? await client.Proxy.GetCurrentUserIdAsync().WaitAsync(InitTimeout)
                : 0;
            Log.Info($"Session worker ready for AppId {appId}: {LastInitResult}");
        }
        catch (TimeoutException)
        {
            // A wedged SteamAPI.Init would otherwise keep the shell waiting forever.
            Log.Error($"Steam worker for AppId {appId} did not initialize within {InitTimeout.TotalSeconds:0}s, killing it");
            await StopCoreAsync();
            LastInitResult = SteamInitResult.SteamNotRunning;
        }
        catch (Exception ex)
        {
            Log.Error($"Worker initialization RPC failed for AppId {appId}: {ex.Message}");
            LastInitResult = SteamInitResult.SteamNotRunning;
            CurrentUserId = 0;
        }

        return LastInitResult;
    }

    private async Task StopCoreAsync()
    {
        CancelIdleTimer();
        var wasStandby = _standbyAppId != 0;
        _standbyAppId = 0;

        if (_client is null)
        {
            // Also cancels a crash respawn waiting in its backoff.
            ActiveAppId = 0;
            if (wasStandby)
            {
                LastInitResult = SteamInitResult.SteamNotRunning;
                CurrentUserId = 0;
            }
            return;
        }

        // Flag the shutdown before tearing down so the Exited watcher stays
        // silent - otherwise each session switch would look like a crash.
        _client.MarkIntentionalShutdown();

        // A busy worker must not block a session switch: past the timeout it's disposed (killed) anyway.
        try { await _client.Proxy.ShutdownAsync().WaitAsync(ShutdownTimeout); } catch { }
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
    private async Task HandleCrashAsync(SteamWorkerClient crashed, uint crashedAppId)
    {
        // Ignore late events from a client that has already been replaced or torn down.
        if (!ReferenceEquals(_client, crashed)) return;

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
                Log.Error($"Worker crashed {_restartCount} times in the last {RestartWindow.TotalSeconds}s - giving up recovery for AppId {crashedAppId}.");
                attempt = -1;
                delaySeconds = 0;
            }
            else
            {
                attempt = _restartCount;
                delaySeconds = _restartCount switch { 1 => 1, 2 => 2, _ => 5 };
            }
        }

        await _lifecycle.WaitAsync();
        try
        {
            // A switch or stop may have replaced the client meanwhile.
            if (!ReferenceEquals(_client, crashed)) return;

            // Drop the dead client; ActiveAppId stays set so a switch during the backoff is detectable.
            _client = null;
            LastInitResult = SteamInitResult.SteamNotRunning;
            CurrentUserId = 0;
            try { await crashed.DisposeAsync(); } catch { }
        }
        finally
        {
            _lifecycle.Release();
        }

        if (attempt < 0)
        {
            try { WorkerUnrecoverable?.Invoke(); } catch { }
            return;
        }

        Log.Warning($"Steam worker crashed for AppId {crashedAppId} - respawn attempt {attempt}/{MaxRestartsPerWindow} in {delaySeconds}s.");
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

        try
        {
            SteamInitResult result;
            await _lifecycle.WaitAsync();
            try
            {
                // Session switched, stopped or already respawned during the backoff.
                if (_client is not null || ActiveAppId != crashedAppId)
                {
                    Log.Debug($"Session no longer on a dead AppId {crashedAppId} worker - aborting respawn.");
                    return;
                }
                result = await StartCoreAsync(crashedAppId);
            }
            finally
            {
                _lifecycle.Release();
            }

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