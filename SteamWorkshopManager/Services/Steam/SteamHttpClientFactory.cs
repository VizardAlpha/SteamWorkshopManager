using System;
using System.Net;
using System.Net.Http;

namespace SteamWorkshopManager.Services.Steam;

/// <summary>
/// Creates HttpClient instances pre-configured for Steam requests,
/// including cookies to bypass mature content age gates.
/// </summary>
public static class SteamHttpClientFactory
{
    public static HttpClient Create()
    {
        var handler = new HttpClientHandler();
        var steamUri = new Uri("https://steamcommunity.com");
        handler.CookieContainer.Add(steamUri, new Cookie("birthtime", "28801"));
        handler.CookieContainer.Add(steamUri, new Cookie("wants_mature_content", "1"));
        handler.CookieContainer.Add(steamUri, new Cookie("mature_content", "1"));
        handler.CookieContainer.Add(steamUri, new Cookie("lastagecheckage", "1-0-1990"));

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", "SteamWorkshopManager/1.0");
        return client;
    }
}
