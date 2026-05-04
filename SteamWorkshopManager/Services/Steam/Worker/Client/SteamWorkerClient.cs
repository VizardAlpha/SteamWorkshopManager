using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam.Worker.Contracts;
using SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

namespace SteamWorkshopManager.Services.Steam.Worker.Client;

/// <summary>
/// Shell-side handle to a <see cref="SteamWorkerHost"/> child process.
///
/// Responsibilities:
///   1. Create a duplex named pipe — the shell is the pipe server, so the
///      pipe exists before the worker tries to connect, avoiding races.
///   2. Spawn the current executable with <c>--steam-worker</c> + args.
///   3. Accept the worker's RPC connection and expose a typed proxy through
///      <see cref="Proxy"/>.
///   4. Clean up everything (pipe, RPC, process) in <see cref="DisposeAsync"/>.
///
/// A single instance drives a single worker. Switching sessions disposes the
/// existing client and creates a new one with the new AppId.
/// </summary>
public sealed class SteamWorkerClient : IAsyncDisposable
{
    private static readonly Logger Log = LogService.GetLogger<SteamWorkerClient>();

    private NamedPipeServerStream? _pipe;
    private JsonRpc? _rpc;
    private Process? _process;
    private volatile bool _intentionalShutdown;

    /// <summary>Typed proxy that marshals calls to the worker over the pipe.</summary>
    public ISteamWorker Proxy { get; private set; } = null!;

    /// <summary>
    /// <c>true</c> once the worker has connected and the proxy is usable.
    /// </summary>
    public bool IsConnected => _rpc is not null && !_rpc.IsDisposed;

    /// <summary>
    /// Fired when the worker process exits without a preceding
    /// <see cref="MarkIntentionalShutdown"/> call — i.e. a crash. Handlers run
    /// on the <see cref="Process.Exited"/> threadpool callback, so they must
    /// not block and should dispatch async work on their own.
    /// </summary>
    public event Action? UnexpectedExit;

    /// <summary>
    /// Signals that the next process exit is expected (session switch,
    /// application close). Prevents <see cref="UnexpectedExit"/> from firing
    /// during the disposal sequence.
    /// </summary>
    public void MarkIntentionalShutdown() => _intentionalShutdown = true;

    /// <summary>
    /// Spawns a worker for the given AppId and waits for it to connect.
    /// Throws if the worker fails to connect within 10 seconds.
    /// </summary>
    public async Task StartAsync(uint appId, CancellationToken ct = default)
    {
        var pipeName = $"swm-worker-{Guid.NewGuid():N}";
        var workerArgs = new SteamWorkerArgs(pipeName, appId, Environment.ProcessId);

        _pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var exePath = Process.GetCurrentProcess().MainModule?.FileName
                      ?? throw new InvalidOperationException("Unable to locate host executable path.");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = workerArgs.ToCommandLine(),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Steamworks reads SteamAppId from the env var before falling back to
        // steam_appid.txt. Setting it here scopes the worker to exactly the
        // AppId the shell asked for, regardless of what's on disk.
        psi.EnvironmentVariables["SteamAppId"] = appId.ToString();

        _process = Process.Start(psi)
                   ?? throw new InvalidOperationException("Failed to spawn Steam worker process.");

        // Wire the exit-watcher before we block on the pipe handshake — if the
        // child dies during startup the event fires and we surface a crash
        // instead of hanging on a never-completing connect.
        _process.EnableRaisingEvents = true;
        _process.Exited += OnProcessExited;

        Log.Info($"Spawned Steam worker PID={_process.Id} for AppId={appId} pipe={pipeName}");

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        await _pipe.WaitForConnectionAsync(connectTimeout.Token);

        _rpc = JsonRpc.Attach(_pipe);
        Proxy = _rpc.Attach<ISteamWorker>();

        Log.Info("Steam worker RPC channel attached");

        // Fire-and-forget: the worker holds this call open for the lifetime
        // of the session so its IProgress<LogEntryDto> sink stays valid.
        _ = Proxy.SetLogSinkAsync(
            new Progress<LogEntryDto>(IngestWorkerLog),
            LogService.Instance.IsDebugEnabled);
    }

    private static void IngestWorkerLog(LogEntryDto dto) =>
        LogService.Instance.IngestRemote(
            (LogLevel)dto.Level,
            $"{dto.Source}:worker",
            dto.Message,
            dto.Exception,
            dto.TimestampUtc);

    public async ValueTask DisposeAsync()
    {
        // Any exit from here on is by definition intentional — make sure the
        // Exited handler stays silent even if the caller forgot to mark.
        _intentionalShutdown = true;
        if (_process is not null) _process.Exited -= OnProcessExited;

        try { _rpc?.Dispose(); } catch { }
        try { if (_pipe is not null) await _pipe.DisposeAsync(); } catch { }

        if (_process is { HasExited: false } p)
        {
            try { p.Kill(entireProcessTree: false); } catch { }
            try { await p.WaitForExitAsync(); } catch { }
        }

        _process?.Dispose();
        _rpc = null;
        _pipe = null;
        _process = null;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_intentionalShutdown) return;

        var pid = (sender as Process)?.Id;
        var exitCode = TryReadExitCode(sender as Process);
        Log.Warning($"Steam worker PID={pid} exited unexpectedly (code {exitCode})");
        try { UnexpectedExit?.Invoke(); } catch (Exception ex) { Log.Debug($"UnexpectedExit handler threw: {ex.Message}"); }
    }

    private static string TryReadExitCode(Process? p)
    {
        try { return p?.ExitCode.ToString() ?? "n/a"; }
        catch { return "n/a"; }
    }
}
