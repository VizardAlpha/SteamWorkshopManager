using System;
using System.Threading.Tasks;

namespace SteamWorkshopManager.Services.Telemetry;

public interface ITelemetryService
{
    /// <summary>
    /// Stable random identifier for this install. Surfaced to the user in
    /// Settings → Privacy so it can be quoted in a GDPR deletion request.
    /// </summary>
    Guid InstanceId { get; }

    void Track(string eventType, uint? steamAppId = null);

    Task FlushAsync();
}

public static class TelemetryEventTypes
{
    public const string AppStart = "app_start";
    public const string ModCreated = "mod_created";
    public const string ModUpdated = "mod_updated";
    public const string ModDeleted = "mod_deleted";
    public const string SessionAdded = "session_added";
}
