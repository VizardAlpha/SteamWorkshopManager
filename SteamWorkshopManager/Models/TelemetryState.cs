using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SteamWorkshopManager.Models;

public class TelemetryState
{
    public Guid InstanceId { get; set; }

    public List<TelemetryQueuedEvent> Queue { get; set; } = [];
}

public class TelemetryQueuedEvent
{
    public string Type { get; set; } = string.Empty;

    public uint? SteamAppId { get; set; }

    public DateTime Timestamp { get; set; }
}

public class TelemetryPayload
{
    [JsonPropertyName("instanceId")]
    public Guid InstanceId { get; set; }

    [JsonPropertyName("os")]
    public string Os { get; set; } = string.Empty;

    [JsonPropertyName("osVersion")]
    public string? OsVersion { get; set; }

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("events")]
    public List<TelemetryPayloadEvent> Events { get; set; } = [];
}

public class TelemetryPayloadEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("steamAppId")]
    public uint? SteamAppId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
