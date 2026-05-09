using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Session;

/// <summary>
/// Removes everything tied to a session: the session JSON, the AppId-keyed
/// caches (headers, icons, tags), the downloaded mod versions and the drafts
/// authored against that AppId. AppId-keyed assets are only purged when no
/// other session still references the same AppId — two sessions on the same
/// game would otherwise lose shared cache for both.
/// </summary>
public sealed class SessionCleanupService
{
    private static readonly Logger Log = LogService.GetLogger<SessionCleanupService>();

    private readonly DraftService _draftService;

    public SessionCleanupService(DraftService draftService)
    {
        _draftService = draftService;
    }

    /// <summary>
    /// Inspects what would be removed if <paramref name="session"/> were
    /// deleted, given the full list of sessions still on disk. Pure read —
    /// nothing is touched.
    /// </summary>
    public SessionCleanupReport BuildReport(WorkshopSession session, IReadOnlyList<WorkshopSession> allSessions)
    {
        var sharedAppId = allSessions.Any(s => s.Id != session.Id && s.AppId == session.AppId);

        var (workshopSize, workshopVersionCount) = MeasureWorkshopFolder(session.AppId);
        var (draftsSize, draftsCount) = MeasureDrafts(session.AppId);

        long cacheSize = 0;
        if (!sharedAppId)
            cacheSize = SafeFileSize(AppPaths.HeaderForApp(session.AppId))
                       + SafeFileSize(AppPaths.IconForApp(session.AppId))
                       + SafeFileSize(AppPaths.TagsForApp(session.AppId));

        return new SessionCleanupReport
        {
            AppIdSharedWithOtherSession = sharedAppId,
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
            var sharedAppId = allSessions.Any(s => s.Id != session.Id && s.AppId == session.AppId);

            TryDeleteFile(AppPaths.SessionFile(session.Id));

            if (!sharedAppId)
            {
                TryDeleteFile(AppPaths.HeaderForApp(session.AppId));
                TryDeleteFile(AppPaths.IconForApp(session.AppId));
                TryDeleteFile(AppPaths.TagsForApp(session.AppId));
                TryDeleteDirectory(AppPaths.WorkshopForApp(session.AppId));
            }

            // Drafts are stored by tempId, not AppId, so we have to enumerate
            // and filter. Drafts shared between two sessions on the same AppId
            // are kept when sharedAppId is true.
            if (!sharedAppId)
            {
                foreach (var draft in _draftService.ListForApp(session.AppId))
                    _draftService.Delete(draft.TempId);
            }

            Log.Info($"Session purged: {session.Name} ({session.Id}, AppId={session.AppId}, sharedAppId={sharedAppId})");
        });
    }

    private static (long bytes, int versionCount) MeasureWorkshopFolder(uint appId)
    {
        var folder = AppPaths.WorkshopForApp(appId);
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

    private static long SafeFileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
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
    public required bool AppIdSharedWithOtherSession { get; init; }
    public required long CacheBytes { get; init; }
    public required int DownloadedVersionCount { get; init; }
    public required long DownloadedBytes { get; init; }
    public required int DraftCount { get; init; }
    public required long DraftBytes { get; init; }

    public long TotalBytes => CacheBytes + DownloadedBytes + DraftBytes;

    public bool HasCache => !AppIdSharedWithOtherSession;
    public bool HasDownloads => DownloadedVersionCount > 0;
    public bool HasDrafts => DraftCount > 0;

    public string DownloadedBytesDisplay => Formatters.Bytes(DownloadedBytes);
    public string DraftBytesDisplay => Formatters.Bytes(DraftBytes);
    public string TotalBytesDisplay => Formatters.Bytes(TotalBytes);
}