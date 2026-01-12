using System.Collections.Generic;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Repository for managing workshop sessions.
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// Gets all available sessions.
    /// </summary>
    Task<List<WorkshopSession>> GetAllSessionsAsync();

    /// <summary>
    /// Gets the currently active session, or null if none.
    /// </summary>
    Task<WorkshopSession?> GetActiveSessionAsync();

    /// <summary>
    /// Gets a session by its ID.
    /// </summary>
    Task<WorkshopSession?> GetSessionAsync(string sessionId);

    /// <summary>
    /// Saves a session (creates or updates).
    /// </summary>
    Task SaveSessionAsync(WorkshopSession session);

    /// <summary>
    /// Deletes a session by its ID.
    /// </summary>
    Task DeleteSessionAsync(string sessionId);

    /// <summary>
    /// Sets the active session by ID.
    /// </summary>
    Task SetActiveSessionAsync(string sessionId);

    /// <summary>
    /// Gets the ID of the currently active session.
    /// </summary>
    string? GetActiveSessionId();

    /// <summary>
    /// Checks if any sessions exist.
    /// </summary>
    Task<bool> HasSessionsAsync();
}
