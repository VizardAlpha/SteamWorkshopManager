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

/// <summary>
/// Versioned gate for the in-app telemetry consent UI. Bumped whenever
/// the data we collect, the destination, or the legal basis changes —
/// any user whose stored <c>TelemetryConsentVersion</c> is below this
/// must see the consent modal again before any data is dispatched.
/// </summary>
public static class TelemetryConsent
{
    /// <summary>
    /// Version 1: introduces the public dashboard at swm-stats.com,
    /// CF-derived country code, and the opt-out workflow. Existing
    /// 1.4.x installs upgrade with version 0 and must reconfirm.
    /// </summary>
    public const int RequiredVersion = 1;
}
