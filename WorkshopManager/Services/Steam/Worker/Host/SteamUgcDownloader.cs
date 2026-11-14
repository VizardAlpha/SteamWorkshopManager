using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Steamworks;
using WorkshopManager.Services.Log;

namespace WorkshopManager.Services.Steam.Worker.Host;

/// <summary>
/// Has the Steam client download a Workshop item's current content, the way a subscription would,
/// and returns where Steam put it. Worker-only.
/// </summary>
internal sealed class SteamUgcDownloader(SteamService steam)
{
    private static readonly Logger Log = LogService.GetLogger<SteamUgcDownloader>();

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    // Steam queues the download before it shows any state for it.
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(5);

    private const uint PathBufferSize = 1024;

    public async Task<DownloadItemOutcome> DownloadAsync(PublishedFileId_t fileId, IProgress<UploadProgress>? progress)
    {
        if (!steam.IsInitialized) return DownloadItemOutcome.Failed;

        if (!IsCurrent(fileId))
        {
            // Not high priority: it would pause the user's other Steam downloads.
            if (!SteamUGC.DownloadItem(fileId, false))
            {
                Log.Error($"Steam refused to download item {fileId}");
                return DownloadItemOutcome.Failed;
            }

            if (!await WaitForDownloadAsync(fileId, progress)) return DownloadItemOutcome.Failed;
        }

        if (!SteamUGC.GetItemInstallInfo(fileId, out var size, out var folder, PathBufferSize, out _)
            || string.IsNullOrEmpty(folder))
        {
            Log.Error($"Item {fileId} is downloaded but Steam gave no install folder");
            return DownloadItemOutcome.Failed;
        }

        Log.Info($"Item {fileId} is available at {folder} ({size} bytes)");
        return new DownloadItemOutcome(true, folder, (long)size);
    }

    private static EItemState State(PublishedFileId_t fileId) => (EItemState)SteamUGC.GetItemState(fileId);

    private static bool IsCurrent(PublishedFileId_t fileId)
    {
        var state = State(fileId);
        return state.HasFlag(EItemState.k_EItemStateInstalled)
               && !state.HasFlag(EItemState.k_EItemStateNeedsUpdate)
               && !IsBusy(state);
    }

    private static bool IsBusy(EItemState state) =>
        state.HasFlag(EItemState.k_EItemStateDownloading) || state.HasFlag(EItemState.k_EItemStateDownloadPending);

    /// <summary>Polls the item state: Steam reports a download through callbacks the worker does not route.</summary>
    private static async Task<bool> WaitForDownloadAsync(PublishedFileId_t fileId, IProgress<UploadProgress>? progress)
    {
        var started = Stopwatch.StartNew();
        var sinceProgress = Stopwatch.StartNew();
        ulong lastBytes = 0;
        var seenBusy = false;

        while (true)
        {
            await Task.Delay(PollInterval);

            var state = State(fileId);
            if (IsCurrent(fileId)) return true;

            if (IsBusy(state))
            {
                seenBusy = true;
                if (SteamUGC.GetItemDownloadInfo(fileId, out var done, out var total) && total > 0)
                {
                    if (done != lastBytes)
                    {
                        lastBytes = done;
                        sinceProgress.Restart();
                    }
                    progress?.Report(new UploadProgress("Downloading", done, total));
                }
            }
            else if (seenBusy || started.Elapsed > StartGrace)
            {
                // It stopped, or never started, without ending up installed: Steam gave up on it.
                Log.Error($"Download of item {fileId} ended without the item being installed (state {state})");
                return false;
            }

            if (sinceProgress.Elapsed > StallTimeout)
            {
                Log.Error($"Download of item {fileId} stalled: no progress from Steam");
                return false;
            }
        }
    }
}
