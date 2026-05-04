using System;

namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// Wire format for a log entry crossing the worker → shell RPC channel. The
/// worker forwards every <c>LogService</c> write through this DTO so the
/// shell remains the only process that touches the on-disk log file —
/// removing the cross-process write race that previously made worker logs
/// silently disappear.
/// </summary>
/// <param name="Level">Maps to <c>LogLevel</c>: 0=Debug, 1=Info, 2=Warning, 3=Error.</param>
/// <param name="Source">Logger name (typically the class name).</param>
/// <param name="Message">Sanitized message body.</param>
/// <param name="Exception">Pre-serialized stack trace or null.</param>
/// <param name="TimestampUtc">UTC timestamp captured at the call site so
/// out-of-order delivery doesn't reorder entries on disk.</param>
public sealed record LogEntryDto(
    int Level,
    string Source,
    string Message,
    string? Exception,
    DateTime TimestampUtc
);