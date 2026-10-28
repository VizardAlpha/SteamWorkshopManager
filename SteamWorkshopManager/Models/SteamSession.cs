using System.Text.Json.Serialization;

namespace SteamWorkshopManager.Models;

/// <summary>Session publishing to one game's Steam Workshop.</summary>
public sealed class SteamSession : WorkshopSession
{
    /// <summary>Steam App ID for this game (e.g., 1162750 for Songs of Syx).</summary>
    public uint AppId { get; set; }

    [JsonIgnore]
    public override WorkshopPlatform Platform => WorkshopPlatform.Steam;

    public override bool IsSameGame(WorkshopSession other) => other is SteamSession steam && steam.AppId == AppId;

    [JsonIgnore]
    public override string GameIdDisplay => $"AppID: {AppId}";

    public override string ToString() => $"{GameName ?? Name} (Steam app {AppId})";
}
