using System;
using System.IO;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Core;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Workshop;

namespace SteamWorkshopManager.Services.Session;

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

    public SessionManager(
        ISessionRepository sessionRepository,
        WorkshopTagsService tagsService,
        AppIdValidator appIdValidator,
        SessionHost sessionHost)
    {
        _sessionRepository = sessionRepository;
        _tagsService = tagsService;
        _appIdValidator = appIdValidator;
        _sessionHost = sessionHost;
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
            Log.Info($"Fetching tags for new session: {gameName}");
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

        // Update global app state only after the worker is live, so any code
        // reading AppConfig.CurrentSession sees a consistent snapshot.
        AppConfig.Clear();
        AppConfig.Initialize(newSession);

        newSession.LastUsedAt = DateTime.UtcNow;
        try { await _sessionRepository.SaveSessionAsync(newSession); }
        catch (Exception ex) { Log.Debug($"Failed to persist LastUsedAt: {ex.Message}"); }

        return initResult;
    }

    /// <summary>
    /// Updates the steam_appid.txt file with the new AppId.
    /// Steam reads this from the working directory, so we write to multiple locations.
    /// </summary>
    public static async Task UpdateSteamAppIdFileAsync(uint appId)
    {
        try
        {
            var appIdContent = appId.ToString();

            // Write to AppContext.BaseDirectory
            var basePath = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
            await File.WriteAllTextAsync(basePath, appIdContent);
            Log.Info($"Updated steam_appid.txt to {appId} at {basePath}");

            // Also write to current working directory if different
            var workingDir = Environment.CurrentDirectory;
            if (!string.Equals(workingDir, AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                var workingPath = Path.Combine(workingDir, "steam_appid.txt");
                await File.WriteAllTextAsync(workingPath, appIdContent);
                Log.Info($"Also updated steam_appid.txt at {workingPath}");
            }
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
        Log.Info($"Refreshing tags for session: {session.Name}");

        var tagsResult = await _tagsService.GetTagsForAppAsync(session.AppId, forceRefresh: true);
        session.TagsByCategory = tagsResult.TagsByCategory;
        session.DropdownCategories = tagsResult.DropdownCategories;
        session.TagsLastUpdated = DateTime.UtcNow;

        await _sessionRepository.SaveSessionAsync(session);

        // Update AppConfig if this is the current session
        if (AppConfig.CurrentSession?.Id == session.Id)
        {
            AppConfig.UpdateSession(session);
        }

        Log.Info($"Tags refreshed: {tagsResult.TagsByCategory.Count} categories");
    }

    /// <summary>
    /// Ensures a session exists for the current steam_appid.txt value.
    /// Used for backwards compatibility.
    /// </summary>
    public async Task<WorkshopSession?> EnsureSessionFromAppIdFileAsync()
    {
        try
        {
            var appIdPath = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
            if (!File.Exists(appIdPath))
            {
                return null;
            }

            var content = await File.ReadAllTextAsync(appIdPath);
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
