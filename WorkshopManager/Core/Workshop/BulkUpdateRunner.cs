using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using WorkshopManager.Services.Steam;

namespace WorkshopManager.Core.Workshop;

/// <summary>Where a bulk update stands. Bytes cover the whole batch, not the current mod.</summary>
public sealed record BulkUpdateProgress(
    int Index, int Total, string Title, long BytesDone, long BytesTotal, TimeSpan? RetryIn = null);

/// <summary>A mod that was not updated, with Steam's answer when there was one.</summary>
public sealed record BulkUpdateFailure(string Title, EResult? Result);

public sealed record BulkUpdateSummary(
    int Updated, IReadOnlyList<BulkUpdateFailure> Failed, bool Cancelled, bool LegalAgreementPending);

/// <summary>
/// Sends the mods of a bulk update one after the other. Steam publishes no rate limit for updates,
/// so the pace adapts: a short pause between mods, and when Steam pushes back the whole queue
/// freezes on the current mod and retries it. A mod that is finally given up on is recorded as
/// failed and the queue moves on: one refusal says nothing about the next mod.
/// </summary>
public sealed class BulkUpdateRunner
{
    public static readonly TimeSpan PauseBetweenMods = TimeSpan.FromSeconds(2);

    /// <summary>Waits after a rate limit. Quick retries make Steam's block worse; the last wait repeats.</summary>
    public static readonly IReadOnlyList<TimeSpan> RateLimitWaits =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
    ];

    /// <summary>Longest time the queue stays frozen on one rate-limited mod before moving on.</summary>
    public static readonly TimeSpan MaxFrozen = TimeSpan.FromHours(2);

    /// <summary>Waits when Steam is briefly unavailable, then the mod is given up on.</summary>
    public static readonly IReadOnlyList<TimeSpan> OutageWaits =
    [
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
    ];

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public BulkUpdateRunner(Func<TimeSpan, CancellationToken, Task>? delay = null) => _delay = delay ?? Task.Delay;

    /// <summary>Steam asks to slow down.</summary>
    public static bool IsRateLimit(EResult? result) => result is
        EResult.k_EResultRateLimitExceeded or EResult.k_EResultTooManyPending;

    /// <summary>Steam is briefly unavailable.</summary>
    public static bool IsOutage(EResult? result) => result is
        EResult.k_EResultBusy or EResult.k_EResultTimeout or EResult.k_EResultServiceUnavailable
        or EResult.k_EResultLockingFailed or EResult.k_EResultTryAnotherCM;

    /// <param name="send">Uploads one mod and reports its own bytes.</param>
    public async Task<BulkUpdateSummary> RunAsync(
        IReadOnlyList<BulkUpdateEntry> entries,
        Func<BulkUpdateEntry, IProgress<UploadProgress>, Task<UpdateItemOutcome>> send,
        IProgress<BulkUpdateProgress> progress,
        CancellationToken ct)
    {
        var total = entries.Sum(e => e.SizeBytes);
        long doneBefore = 0;
        var updated = 0;
        var failed = new List<BulkUpdateFailure>();
        var legalAgreement = false;

        BulkUpdateSummary Cancelled() => new(updated, failed, Cancelled: true, legalAgreement);

        for (var i = 0; i < entries.Count; i++)
        {
            if (ct.IsCancellationRequested) return Cancelled();

            var entry = entries[i];
            var index = i + 1;
            var before = doneBefore;
            void Report(long ownBytes, TimeSpan? retryIn = null) => progress.Report(new BulkUpdateProgress(
                index, entries.Count, entry.Item.Title, before + Math.Clamp(ownBytes, 0, entry.SizeBytes), total, retryIn));

            var own = new Helpers.DirectProgress<UploadProgress>(p => Report((long)p.BytesProcessed));
            Report(0);

            UpdateItemOutcome outcome;
            var rateLimitHits = 0;
            var outageHits = 0;
            var frozen = TimeSpan.Zero;
            while (true)
            {
                outcome = await send(entry, own);
                if (outcome.Success) break;

                TimeSpan wait;
                if (IsRateLimit(outcome.Result))
                {
                    wait = RateLimitWaits[Math.Min(rateLimitHits++, RateLimitWaits.Count - 1)];
                    if (frozen + wait > MaxFrozen) break;
                    frozen += wait;
                }
                else if (IsOutage(outcome.Result) && outageHits < OutageWaits.Count)
                {
                    wait = OutageWaits[outageHits++];
                }
                else
                {
                    break;
                }

                // Nothing else is sent meanwhile: the queue is sequential.
                if (!await WaitAsync(wait, remaining => Report(0, remaining), ct)) return Cancelled();
            }

            if (outcome.Success)
            {
                updated++;
                legalAgreement |= outcome.NeedsLegalAgreement;
            }
            else
            {
                failed.Add(new BulkUpdateFailure(entry.Item.Title, outcome.Result));
            }

            doneBefore += entry.SizeBytes;
            Report(entry.SizeBytes);

            if (i < entries.Count - 1 && !await WaitAsync(PauseBetweenMods, null, ct)) return Cancelled();
        }

        return new BulkUpdateSummary(updated, failed, Cancelled: false, legalAgreement);
    }

    /// <summary>Waits second by second so the countdown stays live. False when cancelled.</summary>
    private async Task<bool> WaitAsync(TimeSpan duration, Action<TimeSpan>? tick, CancellationToken ct)
    {
        var step = TimeSpan.FromSeconds(1);
        try
        {
            for (var remaining = duration; remaining > TimeSpan.Zero; remaining -= step)
            {
                tick?.Invoke(remaining);
                await _delay(remaining < step ? remaining : step, ct);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
