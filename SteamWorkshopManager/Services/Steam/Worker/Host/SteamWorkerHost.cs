using System.IO.Pipes;
using System.Threading.Tasks;
using StreamJsonRpc;
using SteamWorkshopManager.Services.Steam.Worker.Contracts;

namespace SteamWorkshopManager.Services.Steam.Worker.Host;

/// <summary>
/// Entry point for the Steam worker mode.
///
/// The shell spawns the same binary with <c>--steam-worker</c>, a unique
/// named-pipe name, and the target Steam AppId. This host connects back to
/// the shell's pipe, binds an <see cref="ISteamWorker"/> implementation to the
/// JSON-RPC channel, and stays alive until the channel is closed (either the
/// shell disposes the client or the worker is killed on session switch).
/// </summary>
public static class SteamWorkerHost
{
    /// <summary>
    /// Runs the worker loop. Blocks until the RPC connection is terminated.
    ///
    /// The worker's lifetime is tied to the shell purely through the named
    /// pipe: when the shell process exits (clean shutdown, crash, force-kill,
    /// even BSOD), the OS releases its end of the pipe, which makes
    /// <see cref="JsonRpc.Completion"/> resolve and unwinds back here. No
    /// extra parent-watch is needed — the pipe is the same mechanism that
    /// signals every other RPC end-of-life.
    ///
    /// Failures are swallowed because surfacing them to stdout would spawn a
    /// console window on Windows; the shell-side client detects the dead
    /// pipe and reports the failure in its own logs.
    /// </summary>
    public static async Task RunAsync(SteamWorkerArgs args)
    {
        // The worker has its own process-local AppConfig (static classes live
        // per process). Steamworks wrappers inside SteamService read
        // AppConfig.AppId when building UGC queries — if it's zero, every
        // query comes back empty. Seed it from the CLI args.
        SteamWorkshopManager.Services.Core.AppConfig.InitializeAppIdOnly(args.AppId);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                args.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(10_000);

            var target = new SteamWorkerImpl();
            using var rpc = JsonRpc.Attach(pipe, target);
            await rpc.Completion;
        }
        catch
        {
            // Intentionally silent — see RunAsync XML doc.
        }
    }
}