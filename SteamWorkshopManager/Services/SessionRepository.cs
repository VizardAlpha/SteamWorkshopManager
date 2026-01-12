using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services;

[JsonSerializable(typeof(WorkshopSession))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SessionJsonContext : JsonSerializerContext;

/// <summary>
/// Repository for managing workshop sessions stored as JSON files.
/// </summary>
public class SessionRepository : ISessionRepository
{
    private static readonly Logger Log = LogService.GetLogger<SessionRepository>();

    private static readonly string BaseFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager"
    );

    private static readonly string SessionsFolder = Path.Combine(BaseFolder, "sessions");

    private readonly ISettingsService _settingsService;

    public SessionRepository(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        Directory.CreateDirectory(SessionsFolder);
    }

    public string? GetActiveSessionId() => _settingsService.Settings.ActiveSessionId;

    public async Task<List<WorkshopSession>> GetAllSessionsAsync()
    {
        var sessions = new List<WorkshopSession>();

        try
        {
            if (!Directory.Exists(SessionsFolder))
            {
                return sessions;
            }

            var files = Directory.GetFiles(SessionsFolder, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var session = JsonSerializer.Deserialize(json, SessionJsonContext.Default.WorkshopSession);
                    if (session != null)
                    {
                        sessions.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to load session from {file}: {ex.Message}");
                }
            }

            // Sort by last used (most recent first)
            sessions = sessions.OrderByDescending(s => s.LastUsedAt).ToList();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to get all sessions: {ex.Message}");
        }

        return sessions;
    }

    public async Task<WorkshopSession?> GetActiveSessionAsync()
    {
        var activeId = GetActiveSessionId();
        if (string.IsNullOrEmpty(activeId))
        {
            return null;
        }

        return await GetSessionAsync(activeId);
    }

    public async Task<WorkshopSession?> GetSessionAsync(string sessionId)
    {
        try
        {
            var filePath = GetSessionFilePath(sessionId);
            if (!File.Exists(filePath))
            {
                Log.Warning($"Session file not found: {sessionId}");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize(json, SessionJsonContext.Default.WorkshopSession);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load session {sessionId}: {ex.Message}");
            return null;
        }
    }

    public async Task SaveSessionAsync(WorkshopSession session)
    {
        try
        {
            Directory.CreateDirectory(SessionsFolder);
            var filePath = GetSessionFilePath(session.Id);
            var json = JsonSerializer.Serialize(session, SessionJsonContext.Default.WorkshopSession);
            await File.WriteAllTextAsync(filePath, json);
            Log.Debug($"Session saved: {session.Name} ({session.Id})");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save session {session.Id}: {ex.Message}");
            throw;
        }
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        try
        {
            var filePath = GetSessionFilePath(sessionId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Log.Info($"Session deleted: {sessionId}");
            }

            // If we're deleting the active session, clear it
            if (_settingsService.Settings.ActiveSessionId == sessionId)
            {
                _settingsService.Settings.ActiveSessionId = null;
                _settingsService.Save();

                // Set another session as active if available
                var remaining = await GetAllSessionsAsync();
                if (remaining.Count > 0)
                {
                    await SetActiveSessionAsync(remaining[0].Id);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to delete session {sessionId}: {ex.Message}");
            throw;
        }
    }

    public Task SetActiveSessionAsync(string sessionId)
    {
        _settingsService.Settings.ActiveSessionId = sessionId;
        _settingsService.Save();
        Log.Info($"Active session set to: {sessionId}");
        return Task.CompletedTask;
    }

    public async Task<bool> HasSessionsAsync()
    {
        var sessions = await GetAllSessionsAsync();
        return sessions.Count > 0;
    }

    private static string GetSessionFilePath(string sessionId)
    {
        return Path.Combine(SessionsFolder, $"{sessionId}.json");
    }
}
