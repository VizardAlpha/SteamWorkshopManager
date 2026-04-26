using System;

namespace SteamWorkshopManager.Services.Steam.Worker.Contracts;

/// <summary>
/// Strongly typed view of the command-line arguments the shell passes when
/// spawning the worker binary. The shell serializes with
/// <see cref="ToCommandLine"/>; the worker restores with <see cref="TryParse"/>.
/// </summary>
public sealed record SteamWorkerArgs(string PipeName, uint AppId, int ParentPid)
{
    public const string WorkerFlag = "--steam-worker";
    public const string PipeNameFlag = "--pipe-name";
    public const string AppIdFlag = "--app-id";
    public const string ParentPidFlag = "--parent-pid";

    public string ToCommandLine() =>
        $"{WorkerFlag} {PipeNameFlag} {PipeName} {AppIdFlag} {AppId} {ParentPidFlag} {ParentPid}";

    public static bool TryParse(string[] args, out SteamWorkerArgs parsed)
    {
        parsed = default!;

        if (args is null || args.Length == 0) return false;
        if (Array.IndexOf(args, WorkerFlag) < 0) return false;

        var pipeName = ExtractValue(args, PipeNameFlag);
        var appIdRaw = ExtractValue(args, AppIdFlag);
        var parentPidRaw = ExtractValue(args, ParentPidFlag);

        if (string.IsNullOrEmpty(pipeName)) return false;
        if (!uint.TryParse(appIdRaw, out var appId)) return false;
        if (!int.TryParse(parentPidRaw, out var parentPid)) return false;

        parsed = new SteamWorkerArgs(pipeName, appId, parentPid);
        return true;
    }

    private static string? ExtractValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == flag) return args[i + 1];
        }
        return null;
    }
}
