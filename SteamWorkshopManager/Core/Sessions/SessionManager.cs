using System;
using System.IO;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Steam;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Workshop;

namespace SteamWorkshopManager.Core.Sessions;

/// <summary>
/// Manages workshop sessions including switching between them.
/// </summary>
public class SessionManager
{
    private static readonly Logger Log = LogService.GetLogger<SessionManager>();

    private readonly ISessionRepository _sessionRepository;
    private readonly WorkshopTagsService _tagsService;
    private readonly AppIdValidator _appIdValidator;
    private readonly SessionHost _sessionHost;
    private readonly ISessionContext _context;

    public SessionManager(
        ISessionRepository sessionRepository,
        WorkshopTagsService tagsService,
        AppIdValidator appIdValidator,
        SessionHost sessionHost,
        ISessionContext context)
    {
        _sessionRepository = sessionRepository;
        _tagsService = tagsService;
        _appIdValidator = appIdValidator;
        _sessionHost = sessionHost;
        _context = context;
    }

    /// <summary>
    /// Creates a new session from a validated AppId.
    /// </summary>
    public async Task<WorkshopSession> CreateSessionAsync(uint appId, string gameName)
    {
        var session = new WorkshopSession
        {
            Id = Guid.NewGuid().ToString(),
            Name = gameName,
            AppId = appId,
            GameName = gameName,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow
        };

        // Fetch tags for this workshop
        try
        {
            Log.Debug($"Fetching tags for new session: {gameName}");
            var tagsResult = await _tagsService.GetTagsForAppAsync(appId);
            session.TagsByCategory = tagsResult.TagsByCategory;
            session.DropdownCategories = tagsResult.DropdownCategories;
            session.TagsLastUpdated = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to fetch tags for session: {ex.Message}");
            // Continue without tags - user can refresh later
        }

        // Save the session
        await _sessionRepository.SaveSessionAsync(session);
        Log.Info($"Session created: {session.Name} ({session.Id})");

        return session;
    }

    /// <summary>
    /// Switches to a different session in-place: the shell process keeps
    /// running, the Steam worker child process is replaced with one bound to
    /// the new AppId. Callers (<c>MainViewModel</c>) are responsible for
    /// refreshing UI state (item list, hero image, pill bindings) once this
    /// completes.
    /// </summary>
    public async Task<SteamInitResult> SwitchSessionAsync(WorkshopSession newSession)
    {
        Log.Info($"Switching to session: {newSession.Name} (AppId: {newSession.AppId})");

        // Persist the new active session first so a crash mid-switch still
        // lands on the right session at next startup.
        await _sessionRepository.SetActiveSessionAsync(newSession.Id);

        // Kept in sync for backward compatibility with any legacy tooling that
        // reads the file; the worker itself consumes SteamAppId via env var.
        await UpdateSteamAppIdFileAsync(newSession.AppId);

        // Swap the worker: old child dies, new one spawns with the new AppId
        // and calls SteamAPI.Init() inside the fresh process.
        var initResult = await _sessionHost.StartSessionAsync(newSession.AppId);

        // Activated only once the worker is live, so readers see a consistent snapshot.
        _context.Activate(newSession);

        newSession.LastUsedAt = DateTime.UtcNow;
        try { await _sessionRepository.SaveSessionAsync(newSession); }
        catch (Exception ex) { Log.Debug($"Failed to persist LastUsedAt: {ex.Message}"); }

        return initResult;
    }

    /// <summary>
    /// Updates steam_appid.txt next to the binary. Legacy fallback only: the worker
    /// gets its AppId from the SteamAppId environment variable.
    /// </summary>
    public static async Task UpdateSteamAppIdFileAsync(uint appId)
    {
        try
        {
            await File.WriteAllTextAsync(AppPaths.SteamAppIdFile, appId.ToString());
            Log.Debug($"Updated steam_appid.txt to {appId} at {AppPaths.SteamAppIdFile}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to update steam_appid.txt", ex);
            throw;
        }
    }

    /// <summary>
    /// Refreshes tags for the current session.
    /// </summary>
    public async Task RefreshTagsAsync(WorkshopSession session)
    {
        Log.Debug($"Refreshing tags for session: {session.Name}");

        var tagsResult = await _tagsService.GetTagsForAppAsync(session.AppId, forceRefresh: true);
        session.TagsByCategory = tagsResult.TagsByCategory;
        session.DropdownCategories = tagsResult.DropdownCategories;
        session.TagsLastUpdated = DateTime.UtcNow;

        await _sessionRepository.SaveSessionAsync(session);

        if (_context.Current?.Id == session.Id) _context.Update(session);

        Log.Debug($"Tags refreshed: {tagsResult.TagsByCategory.Count} categories");
    }

    /// <summary>
    /// Ensures a session exists for the current steam_appid.txt value.
    /// Used for backwards compatibility.
    /// </summary>
    public async Task<WorkshopSession?> EnsureSessionFromAppIdFileAsync()
    {
        try
        {
            if (!File.Exists(AppPaths.SteamAppIdFile))
            {
                return null;
            }

            var content = await File.ReadAllTextAsync(AppPaths.SteamAppIdFile);
            if (!uint.TryParse(content.Trim(), out var appId))
            {
                return null;
            }

            // Check if we already have a session for this AppId
            var sessions = await _sessionRepository.GetAllSessionsAsync();
            var existing = sessions.Find(s => s.AppId == appId);
            if (existing != null)
            {
                return existing;
            }

            // Create a new session for this AppId
            Log.Info($"Creating session from existing steam_appid.txt: {appId}");

            var result = await _appIdValidator.ValidateAsync(appId);

            if (result.IsValid)
            {
                var session = await CreateSessionAsync(appId, result.GameName ?? $"Game {appId}");
                await _sessionRepository.SetActiveSessionAsync(session.Id);
                return session;
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create session from steam_appid.txt", ex);
            return null;
        }
    }
}
