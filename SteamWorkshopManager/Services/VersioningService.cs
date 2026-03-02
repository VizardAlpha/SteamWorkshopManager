using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;
using Steamworks;

namespace SteamWorkshopManager.Services;

public class VersioningService
{
    private static readonly Logger Log = LogService.GetLogger<VersioningService>();
    private readonly ISteamService _steamService;

    public VersioningService(ISteamService steamService)
    {
        _steamService = steamService;
    }

    public bool IsVersioningEnabled() => _steamService.GetGameBranches().Count > 0;

    public List<GameBranch> GetAvailableBranches() =>
        _steamService.GetGameBranches().Where(b => !b.IsPrivate).ToList();

    public string GetCurrentBranch() => _steamService.GetCurrentBranchName();

    public async Task<List<ModVersionInfo>> GetModVersionsAsync(PublishedFileId_t fileId)
    {
        var versions = new List<ModVersionInfo>();

        try
        {
            var query = SteamUGC.CreateQueryUGCDetailsRequest([fileId], 1);

            var tcs = new TaskCompletionSource<SteamUGCQueryCompleted_t>();
            var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) =>
            {
                if (failure)
                    tcs.SetException(new Exception("Version query failed"));
                else
                    tcs.SetResult(result);
            });

            var handle = SteamUGC.SendQueryUGCRequest(query);
            callResult.Set(handle);

            if (!await PollCallbackAsync(tcs.Task))
            {
                Log.Error("GetModVersions query timed out");
                SteamUGC.ReleaseQueryUGCRequest(query);
                return versions;
            }

            var queryResult = await tcs.Task;
            Log.Info($"Item {fileId}: query result={queryResult.m_eResult}, results={queryResult.m_unNumResultsReturned}");
            if (queryResult.m_eResult != EResult.k_EResultOK || queryResult.m_unNumResultsReturned == 0)
            {
                SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
                return versions;
            }

            var numVersions = SteamUGC.GetNumSupportedGameVersions(queryResult.m_handle, 0);
            Log.Info($"Item {fileId}: query OK, {numVersions} supported game versions found");

            for (uint i = 0; i < numVersions; i++)
            {
                if (SteamUGC.GetSupportedGameVersionData(queryResult.m_handle, 0, i,
                        out var branchMin, out var branchMax, 128))
                {
                    versions.Add(new ModVersionInfo
                    {
                        VersionIndex = i,
                        BranchMin = branchMin,
                        BranchMax = branchMax
                    });
                    Log.Debug($"Version [{i}]: {branchMin} -> {branchMax}");
                }
            }

            SteamUGC.ReleaseQueryUGCRequest(queryResult.m_handle);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to get mod versions", ex);
        }

        return versions;
    }

    private static async Task<bool> PollCallbackAsync(Task task, int timeoutSeconds = 30)
    {
        var timeout = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!task.IsCompleted && DateTime.UtcNow < timeout)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(100);
        }
        return task.IsCompleted;
    }
}
