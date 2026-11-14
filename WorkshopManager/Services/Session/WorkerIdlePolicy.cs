using System;
using WorkshopManager.Models;

namespace WorkshopManager.Services.Session;

/// <summary>How long the Steam worker may stay idle before going on standby.</summary>
public static class WorkerIdlePolicy
{
    public const int MinSeconds = 5;
    public const int MaxSeconds = 3600;
    public const int DefaultSeconds = 60;

    // "After each action" still waits a moment: opening an editor fires several calls in a row.
    private static readonly TimeSpan ActionGrace = TimeSpan.FromSeconds(3);

    /// <summary>Null: the worker stays up.</summary>
    public static TimeSpan? DelayFor(SteamIdleMode mode, int seconds) => mode switch
    {
        SteamIdleMode.AfterEachAction => ActionGrace,
        SteamIdleMode.AfterInactivity => TimeSpan.FromSeconds(Math.Clamp(seconds, MinSeconds, MaxSeconds)),
        _ => null,
    };
}
