# Steam worker

Steamworks keeps process-wide state bound to one AppId. To switch games without restarting the app, all Steamworks calls run in a child process, one per active session.

## Pieces

| Side | Type | Role |
| --- | --- | --- |
| Shell | `SessionHost` | Starts, stops and recovers the worker; exposes the RPC proxy |
| Shell | `SteamWorkerClient` | Spawns the process and owns the named pipe |
| Shell | `WorkerSteamService` | `ISteamService` implementation that forwards every call |
| Contract | `ISteamWorker` + `Contracts/Dtos` | The JSON-RPC surface and its wire types |
| Worker | `SteamWorkerHost` | Entry point for `--steam-worker`, connects to the pipe |
| Worker | `SteamWorkerImpl` | RPC implementation, maps domain models to DTOs |
| Worker | `SteamService` | Steamworks calls for one game (internal, AppId given by constructor) |
| Worker | `SteamCallbackPump` | The single `SteamAPI.RunCallbacks` loop |

## Lifecycle

1. `SessionHost.StartSessionAsync(appId)` spawns the worker with a random pipe name and the AppId, then calls `InitializeAsync` (30 s timeout).
2. The worker sets `SteamAppId`, calls `SteamAPI.Init`, starts the callback pump and waits up to 3 s for the user to be logged on (offline mode is reported as `SteamOffline`).
3. Switching session stops the current worker (5 s shutdown timeout, then the process is killed) and starts a new one.
4. Start, stop and crash recovery are serialized by one lock, so they never overlap.

## Crash recovery

An unexpected exit triggers a respawn with a 1 s, 2 s then 5 s backoff, at most 3 times per minute. A recovery is skipped when the session changed or was stopped during the backoff. When it gives up, the UI shows a disconnected state and the user can switch sessions to retry.

## Callbacks

Steamworks expects one callback pump. `SteamCallbackPump` runs `SteamAPI.RunCallbacks` every 50 ms on a dedicated thread; operations only wait for their CallResult. Uploads poll `GetItemUpdateProgress` and fail only after 5 minutes without any progress.

## Progress and logs

- Upload progress streams back through an `IProgress<T>` argument, which StreamJsonRpc marshals natively. Status strings are localization keys translated by the shell.
- The worker forwards its log entries to the shell, which writes them to the shared log files.
- If the worker dies during an upload, the shell sends the final progress report itself so the UI unlocks.

## Security

The pipe is created with `PipeOptions.CurrentUserOnly` on both ends, so another local user cannot connect to it or impersonate the worker.
