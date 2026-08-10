using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StreamJsonRpc;
using SteamWorkshopManager.Helpers;

namespace SteamWorkshopManager.Services.Log;

/// <summary>
/// Last-resort crash recorder. Writes straight to disk without touching
/// <see cref="LogService"/>, the DI container or Avalonia, so it still works
/// when the failure happened before any of them were ready, or was caused by
/// one of them. Always on, whatever the debug setting says.
/// </summary>
public static class CrashLog
{
    private static readonly object FileLock = new();

    /// <summary>
    /// Installs the process-wide handlers. Call this first thing in Main, before
    /// building the service provider: a throw in there would otherwise kill the
    /// process silently and the user just sees an app that never opens.
    /// </summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain", e.ExceptionObject as Exception, e.IsTerminating ? "terminating" : null);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // Killing the worker on a session switch drops in-flight RPC calls.
            // That is expected shutdown noise, not a crash worth alarming about.
            if (IsWorkerDisconnect(e.Exception))
                LogService.Instance.Warning("Rpc", $"Worker connection dropped: {e.Exception.InnerException?.Message}");
            else
                Write("UnobservedTask", e.Exception);

            e.SetObserved();
        };
    }

    private static bool IsWorkerDisconnect(Exception? exception) => exception switch
    {
        null => false,
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0
            && aggregate.InnerExceptions.All(IsWorkerDisconnect),
        ConnectionLostException => true,
        ObjectDisposedException => true,
        _ => false,
    };

    public static void Write(string origin, Exception? exception, string? note = null)
    {
        try
        {
            var path = Path.Combine(AppPaths.LocalRoot, $"crash_{DateTime.Now:yyyy-MM-dd}.log");
            Directory.CreateDirectory(AppPaths.LocalRoot);

            var header = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CRASH] [{origin}]";
            if (note != null) header += $" ({note})";

            var body = exception?.ToString() ?? "No exception object supplied.";
            lock (FileLock)
            {
                // Shell and worker share this file, so a sharing violation is possible.
                LogService.AppendWithRetry(path, $"{header}{Environment.NewLine}{body}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Nothing left to fall back on.
        }
    }
}