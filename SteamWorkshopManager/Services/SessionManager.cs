using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Steamworks;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Manages workshop sessions including switching between them.
/// </summary>
public class SessionManager
{
    private static readonly Logger Log = LogService.GetLogger<SessionManager>();

    private readonly ISessionRepository _sessionRepository;
    private readonly WorkshopTagsService _tagsService;

    public SessionManager(ISessionRepository sessionRepository, WorkshopTagsService? tagsService = null)
    {
        _sessionRepository = sessionRepository;
        _tagsService = tagsService ?? new WorkshopTagsService();
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
            var tags = await _tagsService.GetTagsForAppAsync(appId);
            session.TagsByCategory = tags;
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
    /// Switches to a different session. This will restart the application.
    /// </summary>
    public async Task SwitchSessionAsync(WorkshopSession newSession)
    {
        Log.Info($"Switching to session: {newSession.Name} (AppId: {newSession.AppId})");

        // 1. Set the new session as active
        await _sessionRepository.SetActiveSessionAsync(newSession.Id);

        // 2. Update steam_appid.txt
        await UpdateSteamAppIdFileAsync(newSession.AppId);

        // 3. Restart the application with the NEW AppId
        RestartApplication(newSession.AppId);
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
    /// Restarts the application with proper Steam cleanup.
    /// </summary>
    /// <param name="newAppId">The AppId for the new session</param>
    private static void RestartApplication(uint newAppId)
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                Log.Error("Cannot restart: ProcessPath is null");
                return;
            }

            Log.Info("Shutting down Steam API before restart...");

            // Run any pending callbacks before shutdown
            try
            {
                SteamAPI.RunCallbacks();
            }
            catch
            {
                // Ignore callback errors during shutdown
            }

            // Properly shutdown Steam API first
            try
            {
                SteamAPI.Shutdown();
                Log.Info("Steam API shut down successfully");
            }
            catch (Exception ex)
            {
                Log.Warning($"Error during Steam shutdown: {ex.Message}");
            }

            // Give Steam time to clean up sockets
            Thread.Sleep(500);

            Log.Info("Restarting application...");

            // Start the new process with SteamAppId environment variable
            // This ensures Steam uses the correct AppId even if steam_appid.txt isn't read
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = false, // Required to set environment variables
                WorkingDirectory = AppContext.BaseDirectory
            };

            // Copy current environment and add/update SteamAppId
            foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
            {
                startInfo.Environment[env.Key?.ToString() ?? ""] = env.Value?.ToString() ?? "";
            }

            // Set the SteamAppId environment variable for the new process
            var appIdStr = newAppId.ToString();
            startInfo.Environment["SteamAppId"] = appIdStr;
            startInfo.Environment["SteamGameId"] = appIdStr;
            Log.Info($"Setting SteamAppId environment variable to {appIdStr}");

            Process.Start(startInfo);

            // Shutdown the current instance
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to restart application", ex);
        }
    }

    /// <summary>
    /// Refreshes tags for the current session.
    /// </summary>
    public async Task RefreshTagsAsync(WorkshopSession session)
    {
        Log.Info($"Refreshing tags for session: {session.Name}");

        var tags = await _tagsService.GetTagsForAppAsync(session.AppId, forceRefresh: true);
        session.TagsByCategory = tags;
        session.TagsLastUpdated = DateTime.UtcNow;

        await _sessionRepository.SaveSessionAsync(session);

        // Update AppConfig if this is the current session
        if (AppConfig.CurrentSession?.Id == session.Id)
        {
            AppConfig.UpdateSession(session);
        }

        Log.Info($"Tags refreshed: {tags.Count} categories");
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

            var validator = new AppIdValidator();
            var result = await validator.ValidateAsync(appId);

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
