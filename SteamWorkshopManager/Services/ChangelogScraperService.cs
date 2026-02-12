using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Interfaces;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services;

public class ChangelogScraperService
{
    private static readonly Logger Log = LogService.GetLogger<ChangelogScraperService>();

    private static readonly Regex ChangeLogRegex = new(
        @"changeLogs\[\d+\]\s*=\s*(\{""timestamp"".*?""accountid"":\d+\});",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public async Task<List<ChangeLogEntry>> GetChangeLogsAsync(ulong publishedFileId)
    {
        var results = new List<ChangeLogEntry>();

        try
        {
            var url = $"https://steamcommunity.com/sharedfiles/filedetails/changelog/{publishedFileId}";
            Log.Info($"Fetching changelogs from {url}");

            string? html;

            if (SteamAuthService.IsAuthenticated)
            {
                Log.Debug("Using authenticated HttpClient for changelog fetch");
                using var httpClient = SteamAuthService.CreateAuthenticatedHttpClient();
                html = await httpClient.GetStringAsync(url);
            }
            else
            {
                Log.Debug("Using unauthenticated SteamWebClient for changelog fetch");
                await SteamWebClient.InitializeAsync();
                html = await SteamWebClient.GetStringAsync(url);
            }

            if (html == null)
            {
                Log.Warning("Returned null for changelog page");
                return results;
            }

            // First try: extract from <script> tags via regex (authenticated HTML has full JSON with manifest_id)
            var matches = ChangeLogRegex.Matches(html);
            Log.Debug($"Regex found {matches.Count} changelog entries");

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    try
                    {
                        var json = match.Groups[1].Value;
                        json = json.Replace("\\/", "/");
                        var entry = JsonSerializer.Deserialize<ChangeLogEntry>(json);
                        if (entry != null)
                            results.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Failed to deserialize changelog JSON: {ex.Message}");
                    }
                }
            }
            else
            {
                // Fallback: parse HTML divs (no manifest_id — download disabled)
                Log.Debug("Falling back to HTML div parsing (no manifest_id available)");
                results = await ParseHtmlDivsAsync(html);
            }

            results = results.OrderByDescending(e => e.Timestamp).ToList();
            Log.Info($"Parsed {results.Count} changelog entries for file {publishedFileId}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch changelogs for file {publishedFileId}", ex);
        }

        return results;
    }

    private static async Task<List<ChangeLogEntry>> ParseHtmlDivsAsync(string html)
    {
        var results = new List<ChangeLogEntry>();
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync(req => req.Content(html));

        var containers = document.QuerySelectorAll("div.changeLogCtn");
        Log.Debug($"Found {containers.Length} div.changeLogCtn");

        foreach (var container in containers)
        {
            try
            {
                var paragraph = container.QuerySelector("p[id]");
                if (paragraph == null) continue;

                var idStr = paragraph.GetAttribute("id") ?? "";
                if (!long.TryParse(idStr, out var timestamp)) continue;

                results.Add(new ChangeLogEntry
                {
                    Timestamp = timestamp,
                    ChangeDescription = paragraph.InnerHtml.Trim()
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to parse changelog div: {ex.Message}");
            }
        }

        return results;
    }
}
