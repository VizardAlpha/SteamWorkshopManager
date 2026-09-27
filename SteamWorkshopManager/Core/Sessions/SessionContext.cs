using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Core.Sessions;

/// <summary>The session the shell is currently working on. Injected wherever the active game matters.</summary>
public interface ISessionContext
{
    WorkshopSession? Current { get; }

    /// <summary>Steam AppId of <see cref="Current"/>, 0 when no session is active.</summary>
    uint AppId { get; }

    void Activate(WorkshopSession session);

    /// <summary>Replaces the active session object after an edit (tags, custom tags...). Ignored for another id.</summary>
    void Update(WorkshopSession session);

    void Clear();
}

public sealed class SessionContext : ISessionContext
{
    private static readonly Logger Log = LogService.GetLogger<SessionContext>();

    public WorkshopSession? Current { get; private set; }

    public uint AppId => Current?.AppId ?? 0;

    public void Activate(WorkshopSession session)
    {
        Current = session;
        Log.Debug($"Active session: {session.GameName ?? session.Name} (AppId: {session.AppId})");
    }

    public void Update(WorkshopSession session)
    {
        if (Current?.Id != session.Id)
        {
            Log.Warning("Attempted to update session with different ID");
            return;
        }
        Current = session;
    }

    public void Clear()
    {
        Current = null;
        Log.Debug("Active session cleared");
    }
}
