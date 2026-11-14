using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkshopManager.Models;
using WorkshopManager.Services.Steam;

namespace WorkshopManager.Core.Workshop;

public sealed record BulkDownloadSummary(int Downloaded, IReadOnlyList<string> Failed, bool Cancelled);

/// <summary>
/// Downloads the mods of a bulk backup one after the other. A mod that fails is recorded and the
/// queue moves on; cancelling stops before the next mod.
/// </summary>
public sealed class BulkDownloadRunner
{
    /// <param name="fetch">Downloads and stores one mod, reporting the bytes received. False when it failed.</param>
    public async Task<BulkDownloadSummary> RunAsync(
        IReadOnlyList<WorkshopItem> items,
        Func<WorkshopItem, IProgress<UploadProgress>, CancellationToken, Task<bool>> fetch,
        IProgress<BulkUpdateProgress> progress,
        CancellationToken ct)
    {
        var total = items.Sum(i => i.FileSize);
        long doneBefore = 0;
        var downloaded = 0;
        var failed = new List<string>();

        for (var i = 0; i < items.Count; i++)
        {
            if (ct.IsCancellationRequested) return new BulkDownloadSummary(downloaded, failed, Cancelled: true);

            var item = items[i];
            var index = i + 1;
            var before = doneBefore;
            void Report(long ownBytes) => progress.Report(new BulkUpdateProgress(
                index, items.Count, item.Title, before + Math.Clamp(ownBytes, 0, item.FileSize), total));

            Report(0);
            var own = new Helpers.DirectProgress<UploadProgress>(p => Report((long)p.BytesProcessed));

            bool ok;
            try
            {
                ok = await fetch(item, own, ct);
            }
            catch (OperationCanceledException)
            {
                return new BulkDownloadSummary(downloaded, failed, Cancelled: true);
            }

            if (ok) downloaded++;
            else failed.Add(item.Title);

            doneBefore += item.FileSize;
            Report(item.FileSize);
        }

        return new BulkDownloadSummary(downloaded, failed, Cancelled: false);
    }
}
