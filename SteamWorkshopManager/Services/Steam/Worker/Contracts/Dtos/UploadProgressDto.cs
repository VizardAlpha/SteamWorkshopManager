namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// JSON-friendly mirror of <see cref="UploadProgress"/> sent from the worker
/// to the shell via <see cref="System.IProgress{T}"/>. StreamJsonRpc marshals
/// IProgress across the pipe as notifications, so every worker-side Report
/// lands on the shell-side handler with sub-ms latency.
/// </summary>
public sealed record UploadProgressDto(string Status, ulong BytesProcessed, ulong BytesTotal, double PercentHint = 0);