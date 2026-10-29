using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Core.Sessions;

/// <summary>
/// Removes everything tied to a session: the session JSON, its game's cache
/// scope, the downloaded mod versions and, for Steam, the drafts authored
/// against that AppId. Game-keyed assets are only purged when no other session
/// targets the same game - two sessions on the same game would otherwise lose
/// shared cache for both.
/// </summary>
public sealed class SessionCleanupService
{
    private static readonly Logger Log = LogService.GetLogger<SessionCleanupService>();

    private readonly DraftService _draftService;
    private readonly ICacheStore _cache;

    public SessionCleanupService(DraftService draftService, ICacheStore cache)
    {
        _draftService = draftService;
        _cache = cache;
    }

    /// <summary>
    /// Inspects what would be removed if <paramref name="session"/> were
    /// deleted, given the full list of sessions still on disk. Pure read -
    /// nothing is touched.
    /// </summary>
    public SessionCleanupReport BuildReport(WorkshopSession session, IReadOnlyList<WorkshopSession> allSessions)
    {
        var sharedGame = IsGameShared(session, allSessions);

        // Drafts only exist for Steam sessions.
        var steam = session as SteamSession;
        var (workshopSize, workshopVersionCount) = MeasureWorkshopFolder(DownloadsFolder(session));
        var (draftsSize, draftsCount) = steam is null ? (0L, 0) : MeasureDrafts(steam.AppId);

        long cacheSize = 0;
        if (!sharedGame)
        {
            var (platform, scope) = CacheKey.SessionScope(session);
            cacheSize = _cache.MeasureScope(platform, scope);
        }

        return new SessionCleanupReport
        {
            GameSharedWithOtherSession = sharedGame,
            CacheBytes = cacheSize,
            DownloadedVersionCount = workshopVersionCount,
            DownloadedBytes = workshopSize,
            DraftCount = draftsCount,
            DraftBytes = draftsSize,
        };
    }

    /// <summary>
    /// Wipes everything described by <see cref="BuildReport"/>. Callers are
    /// expected to switch the worker off the deleted session beforehand.
    /// </summary>
    public Task PurgeAsync(WorkshopSession session, IReadOnlyList<WorkshopSession> allSessions)
    {
        return Task.Run(() =>
        {
            var sharedGame = IsGameShared(session, allSessions);

            TryDeleteFile(AppPaths.SessionFile(session.Id));

            if (!sharedGame)
            {
                var (platform, scope) = CacheKey.SessionScope(session);
                _cache.RemoveScope(platform, scope);

                TryDeleteDirectory(DownloadsFolder(session));
                if (session is SteamSession steam)
                {
                    // Drafts are stored by tempId, not AppId, so we have to enumerate and filter.
                    foreach (var draft in _draftService.ListForApp(steam.AppId))
                        _draftService.Delete(draft.TempId);
                }
            }

            Log.Info($"Session purged: {session} ({session.Id}, sharedGame={sharedGame})");
        });
    }

    private static bool IsGameShared(WorkshopSession session, IReadOnlyList<WorkshopSession> allSessions) =>
        allSessions.Any(s => s.Id != session.Id && s.IsSameGame(session));

    /// <summary>Where a session's downloaded versions live.</summary>
    private static string DownloadsFolder(WorkshopSession session) => session switch
    {
        SteamSession steam => AppPaths.WorkshopForApp(steam.AppId),
        ModioSession modio => AppPaths.WorkshopForModioGame(modio.GameId),
        _ => throw new ArgumentOutOfRangeException(nameof(session)),
    };

    private static (long bytes, int versionCount) MeasureWorkshopFolder(string folder)
    {
        if (!Directory.Exists(folder)) return (0, 0);

        try
        {
            var dirs = Directory.GetDirectories(folder);
            long total = 0;
            foreach (var d in dirs)
            {
                foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { /* race with delete */ }
                }
            }
            return (total, dirs.Length);
        }
        catch
        {
            return (0, 0);
        }
    }

    private (long bytes, int count) MeasureDrafts(uint appId)
    {
        try
        {
            var drafts = _draftService.ListForApp(appId);
            long total = 0;
            foreach (var d in drafts)
            {
                var folder = Path.Combine(AppPaths.Drafts, d.TempId);
                if (!Directory.Exists(folder)) continue;
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { /* race with delete */ }
                }
            }
            return (total, drafts.Count);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log.Warning($"Failed to delete file {path}: {ex.Message}"); }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { Log.Warning($"Failed to delete directory {path}: {ex.Message}"); }
    }

}

public sealed record SessionCleanupReport
{
    public required bool GameSharedWithOtherSession { get; init; }
    public required long CacheBytes { get; init; }
    public required int DownloadedVersionCount { get; init; }
    public required long DownloadedBytes { get; init; }
    public required int DraftCount { get; init; }
    public required long DraftBytes { get; init; }

    public long TotalBytes => CacheBytes + DownloadedBytes + DraftBytes;

    public bool HasCache => !GameSharedWithOtherSession;
    public bool HasDownloads => DownloadedVersionCount > 0;
    public bool HasDrafts => DraftCount > 0;

    public string DownloadedBytesDisplay => Formatters.Bytes(DownloadedBytes);
    public string DraftBytesDisplay => Formatters.Bytes(DraftBytes);
    public string TotalBytesDisplay => Formatters.Bytes(TotalBytes);
}
