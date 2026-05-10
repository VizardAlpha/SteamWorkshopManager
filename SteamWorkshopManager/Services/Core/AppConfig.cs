using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Core;

/// <summary>
/// Global application configuration that holds the current session's AppId.
/// Initialized at startup from the active session.
/// </summary>
public static class AppConfig
{
    private static readonly ILogService Log = LogService.Instance;

    /// <summary>
    /// The Steam AppId for the current session.
    /// </summary>
    public static uint AppId { get; private set; }

    /// <summary>
    /// The current active session.
    /// </summary>
    public static WorkshopSession? CurrentSession { get; private set; }

    private const string Source = nameof(AppConfig);

    /// <summary>
    /// Initializes AppConfig with the given session.
    /// </summary>
    public static void Initialize(WorkshopSession session)
    {
        CurrentSession = session;
        AppId = session.AppId;
        Log.Info(Source, $"AppConfig initialized: {session.GameName ?? session.Name} (AppId: {AppId})");
    }

    /// <summary>
    /// Initializes only the <see cref="AppId"/> — used inside the Steam worker
    /// process where no full <see cref="WorkshopSession"/> is available but
    /// Steamworks code still reads the AppId from here.
    /// </summary>
    public static void InitializeAppIdOnly(uint appId)
    {
        AppId = appId;
        Log.Info(Source, $"AppConfig initialized (AppId only): {AppId}");
    }

    /// <summary>
    /// Updates the current session (e.g., after modifying tags).
    /// </summary>
    public static void UpdateSession(WorkshopSession session)
    {
        if (CurrentSession?.Id != session.Id)
        {
            Log.Warning(Source, "Attempted to update session with different ID");
            return;
        }
        CurrentSession = session;
    }

    /// <summary>
    /// Clears the current configuration (for restart/switch).
    /// </summary>
    public static void Clear()
    {
        CurrentSession = null;
        AppId = 0;
        Log.Debug(Source, "AppConfig cleared");
    }
}
