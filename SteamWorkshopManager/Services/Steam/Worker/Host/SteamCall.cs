using System;
using System.Threading.Tasks;
using Steamworks;

namespace WorkshopManager.Services.Steam.Worker.Host;

/// <summary>
/// Turns a Steam CallResult into a task. <see cref="SteamCallbackPump"/> completes it;
/// continuations leave the pump thread so they never stall the callback loop.
/// </summary>
internal static class SteamCall
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The pending result, for callers that watch something else while it runs (upload progress).</summary>
    public static Task<T> Start<T>(SteamAPICall_t call, string failureMessage) where T : struct =>
        Start<T>(call, failureMessage, out _);

    /// <summary>Null when Steam does not answer within <paramref name="timeout"/>; throws on an IO failure.</summary>
    public static async Task<T?> AwaitAsync<T>(SteamAPICall_t call, TimeSpan timeout, string failureMessage)
        where T : struct
    {
        var task = Start<T>(call, failureMessage, out var callResult);
        try
        {
            return await task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            // Stops a late answer from reaching a caller that already gave up.
            callResult.Cancel();
            return null;
        }
    }

    /// <summary>
    /// Sends a UGC query. Releases the query handle itself when there is no result to read
    /// (timeout or IO failure); otherwise the caller releases the handle of the result.
    /// </summary>
    public static async Task<SteamUGCQueryCompleted_t?> QueryAsync(
        UGCQueryHandle_t query, TimeSpan timeout, string failureMessage)
    {
        SteamUGCQueryCompleted_t? result;
        try
        {
            result = await AwaitAsync<SteamUGCQueryCompleted_t>(
                SteamUGC.SendQueryUGCRequest(query), timeout, failureMessage);
        }
        catch
        {
            SteamUGC.ReleaseQueryUGCRequest(query);
            throw;
        }

        if (result is null) SteamUGC.ReleaseQueryUGCRequest(query);
        return result;
    }

    private static Task<T> Start<T>(SteamAPICall_t call, string failureMessage, out CallResult<T> callResult)
        where T : struct
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        callResult = CallResult<T>.Create((result, ioFailure) =>
        {
            if (ioFailure) tcs.TrySetException(new SteamQueryException(failureMessage));
            else tcs.TrySetResult(result);
        });
        callResult.Set(call);
        return tcs.Task;
    }
}
