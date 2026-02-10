using System;
using System.Text;
using System.Threading.Tasks;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;
using Steamworks;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Wraps SteamHTTP into async methods with a shared cookie container.
/// </summary>
public static class SteamWebClient
{
    private static readonly Logger Log = new("SteamWebClient", LogService.Instance);
    private static HTTPCookieContainerHandle _cookieContainer;
    private static bool _initialized;

    public static async Task InitializeAsync()
    {
        if (_initialized) return;

        _cookieContainer = SteamHTTP.CreateCookieContainer(true);
        _initialized = true;
    }

    public static async Task<string?> GetStringAsync(string url, int timeoutSeconds = 30)
    {
        Log.Debug($"GET {url}");
        var handle = SteamHTTP.CreateHTTPRequest(EHTTPMethod.k_EHTTPMethodGET, url);
        if (handle == HTTPRequestHandle.Invalid)
        {
            Log.Warning("Failed: Invalid handle");
            return null;
        }

        try
        {
            if (_cookieContainer.m_HTTPCookieContainerHandle != 0)
                SteamHTTP.SetHTTPRequestCookieContainer(handle, _cookieContainer);

            var tcs = new TaskCompletionSource<HTTPRequestCompleted_t>();
            var callResult = CallResult<HTTPRequestCompleted_t>.Create((result, failure) =>
            {
                if (failure)
                    tcs.TrySetException(new Exception("SteamHTTP request failed"));
                else
                    tcs.TrySetResult(result);
            });

            if (!SteamHTTP.SendHTTPRequest(handle, out var apiCall))
            {
                Log.Warning("Failed: SendHTTPRequest returned false");
                return null;
            }

            callResult.Set(apiCall);

            var timeout = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (!tcs.Task.IsCompleted && DateTime.UtcNow < timeout)
            {
                SteamAPI.RunCallbacks();
                await Task.Delay(100);
            }

            if (!tcs.Task.IsCompleted)
            {
                Log.Warning("Request timed out");
                return null;
            }

            var completed = await tcs.Task;

            if (!completed.m_bRequestSuccessful || completed.m_eStatusCode != EHTTPStatusCode.k_EHTTPStatusCode200OK)
            {
                Log.Warning($"Failed: status={completed.m_eStatusCode}, success={completed.m_bRequestSuccessful}");
                return null;
            }

            if (!SteamHTTP.GetHTTPResponseBodySize(handle, out var bodySize) || bodySize == 0)
            {
                Log.Warning("Failed: empty body");
                return null;
            }

            Log.Debug($"OK: {bodySize} bytes");

            var buffer = new byte[bodySize];
            if (!SteamHTTP.GetHTTPResponseBodyData(handle, buffer, bodySize))
            {
                Log.Warning("Failed: GetHTTPResponseBodyData");
                return null;
            }

            return Encoding.UTF8.GetString(buffer);
        }
        finally
        {
            SteamHTTP.ReleaseHTTPRequest(handle);
        }
    }
}
