using System;
using System.Collections.Generic;
using System.Threading;
using WorkshopManager.Models;
using Steamworks;
using WorkshopManager.Services.Log;

namespace WorkshopManager.Services.Steam.Worker.Host;

/// <summary>
/// Steam lifecycle for one game (init, logon, shutdown, branches). Lives in the worker process only;
/// the shell goes through <see cref="SteamWorkerImpl"/>. Workshop reads are in
/// <see cref="SteamUgcQueries"/>, writes in <see cref="SteamUgcPublisher"/>.
/// </summary>
internal sealed class SteamService(uint appId)
{
    private static readonly Logger Log = LogService.GetLogger<SteamService>();

    // Steam can still be reconnecting when Init returns, so a false BLoggedOn
    // is only conclusive after a short grace period.
    private static readonly TimeSpan LogonGrace = TimeSpan.FromSeconds(3);

    // The worker process serves exactly one game.
    private readonly uint _appId = appId;

    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;

    public CSteamID? CurrentUserId => _isInitialized ? SteamUser.GetSteamID() : null;

    public SteamInitResult Initialize()
    {
        if (_isInitialized) return SteamInitResult.Success;

        try
        {
            Log.Info($"Initializing Steam for AppId: {_appId}");

            var appIdStr = _appId.ToString();
            var envAppId = Environment.GetEnvironmentVariable("SteamAppId");
            var envGameId = Environment.GetEnvironmentVariable("SteamGameId");

            if (envAppId != appIdStr)
            {
                Environment.SetEnvironmentVariable("SteamAppId", appIdStr);
                Log.Debug($"Set SteamAppId environment variable to {appIdStr}");
            }
            if (envGameId != appIdStr)
            {
                Environment.SetEnvironmentVariable("SteamGameId", appIdStr);
                Log.Debug($"Set SteamGameId environment variable to {appIdStr}");
            }

            Log.Debug($"Environment SteamAppId: {Environment.GetEnvironmentVariable("SteamAppId") ?? "(not set)"}");
            Log.Debug($"Environment SteamGameId: {Environment.GetEnvironmentVariable("SteamGameId") ?? "(not set)"}");

            var steamRunning = SteamAPI.IsSteamRunning();
            Log.Debug($"Steam running: {steamRunning}");

            _isInitialized = SteamAPI.Init();
            Log.Info($"Steam API Init result: {_isInitialized}");

            if (_isInitialized)
            {
                // Every CallResult below is dispatched by this single pump.
                SteamCallbackPump.Start();

                if (!WaitForLogon(LogonGrace))
                {
                    // Init succeeds against a Steam client in offline mode, so
                    // without this check the app reports a healthy connection and
                    // then shows an empty Workshop list with no explanation.
                    Log.Warning("Steam API initialized but the user is not logged on - offline mode");
                    return SteamInitResult.SteamOffline;
                }

                var steamAppId = SteamUtils.GetAppID();
                Log.Debug($"Steam AppID from SteamUtils: {steamAppId}");

                if (steamAppId.m_AppId != _appId)
                {
                    Log.Warning($"AppId mismatch! Expected: {_appId}, Steam reports: {steamAppId.m_AppId}");
                }

                var currentBranch = GetCurrentBranchName();
                Log.Debug($"Current game branch: {currentBranch}");
                var branches = GetGameBranches();
                Log.Debug($"Versioning: {branches.Count} branches available (enabled={branches.Count > 0})");

                return SteamInitResult.Success;
            }

            if (steamRunning)
            {
                Log.Warning("Steam is running but API init failed - game likely not owned");
                return SteamInitResult.GameNotOwned;
            }

            Log.Warning("Steam API initialization failed - Steam is not running");
            return SteamInitResult.SteamNotRunning;
        }
        catch (Exception ex)
        {
            Log.Error($"Steam initialization exception", ex);
            return SteamInitResult.SteamNotRunning;
        }
    }

    private static bool WaitForLogon(TimeSpan grace)
    {
        var deadline = DateTime.UtcNow + grace;
        while (!SteamUser.BLoggedOn())
        {
            if (DateTime.UtcNow >= deadline) return false;
            Thread.Sleep(200);
        }

        return true;
    }

    public void Shutdown()
    {
        if (!_isInitialized) return;
        Log.Info("Shutting down Steam API");
        SteamCallbackPump.Stop();
        SteamAPI.Shutdown();
        _isInitialized = false;
    }

    public List<GameBranch> GetGameBranches()
    {
        var branches = new List<GameBranch>();
        if (!_isInitialized)
        {
            Log.Warning("GetGameBranches called but Steam is not initialized");
            return branches;
        }

        try
        {
            var totalBetas = SteamApps.GetNumBetas(out var available, out var privateBetas);
            Log.Debug($"Game branches: total={totalBetas}, available={available}, private={privateBetas}");

            for (var i = 0; i < totalBetas; i++)
            {
                if (SteamApps.GetBetaInfo(i, out var flags, out var buildId,
                        out var betaName, 128, out var description, 256, out _))
                {
                    branches.Add(new GameBranch
                    {
                        Name = betaName,
                        Description = description,
                        BuildId = buildId,
                        Flags = flags
                    });
                }
                else
                {
                    Log.Warning($"Failed to get beta info for index {i}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to get game branches", ex);
        }

        return branches;
    }

    public string GetCurrentBranchName()
    {
        if (!_isInitialized) return "public";

        try
        {
            if (SteamApps.GetCurrentBetaName(out var betaName, 128) && !string.IsNullOrEmpty(betaName))
            {
                Log.Debug($"Current beta branch: {betaName}");
                return betaName;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to get current beta name", ex);
        }

        return "public";
    }
}
