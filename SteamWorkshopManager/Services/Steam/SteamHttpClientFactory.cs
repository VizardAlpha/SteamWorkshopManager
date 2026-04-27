using System;
using System.Net;
using System.Net.Http;

namespace SteamWorkshopManager.Services.Steam;

/// <summary>
/// Creates HttpClient instances pre-configured for Steam requests.
/// Centralizes the User-Agent so every outbound request advertises a real
/// identity (Cloudflare blocks empty UA), and offers opt-in extras like the
/// age-gate cookies needed to scrape mature-tagged Workshop pages.
/// </summary>
public static class SteamHttpClientFactory
{
    public static readonly string UserAgent = $"SteamWorkshopManager/{AppInfo.Version}";

    public static HttpClient Create(bool withAgeGateCookies = false, TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler();
        if (withAgeGateCookies)
        {
            var steamUri = new Uri("https://steamcommunity.com");
            handler.CookieContainer.Add(steamUri, new Cookie("birthtime", "28801"));
            handler.CookieContainer.Add(steamUri, new Cookie("wants_mature_content", "1"));
            handler.CookieContainer.Add(steamUri, new Cookie("mature_content", "1"));
            handler.CookieContainer.Add(steamUri, new Cookie("lastagecheckage", "1-0-1990"));
        }

        var client = new HttpClient(handler);
        if (timeout is { } t) client.Timeout = t;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }
}