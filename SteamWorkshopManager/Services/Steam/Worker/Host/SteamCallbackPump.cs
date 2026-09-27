using System;
using System.Threading;
using SteamWorkshopManager.Services.Log;
using Steamworks;

namespace SteamWorkshopManager.Services.Steam.Worker.Host;

/// <summary>
/// The single <c>SteamAPI.RunCallbacks</c> loop of the worker. Steamworks expects one
/// pump; operations only wait for their CallResult instead of pumping themselves.
/// </summary>
internal static class SteamCallbackPump
{
    private static readonly Logger Log = new(nameof(SteamCallbackPump), LogService.Instance);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private static Thread? _thread;
    private static volatile bool _running;

    public static void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "SteamCallbacks" };
        _thread.Start();
    }

    /// <summary>Must run before <c>SteamAPI.Shutdown</c>.</summary>
    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    private static void Run()
    {
        while (_running)
        {
            try
            {
                SteamAPI.RunCallbacks();
            }
            catch (Exception ex)
            {
                Log.Warning($"Steam callback dispatch failed: {ex.Message}");
            }
            Thread.Sleep(Interval);
        }
    }
}
