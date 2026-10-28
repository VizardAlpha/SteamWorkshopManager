using System.Text.Json.Serialization;

namespace SteamWorkshopManager.Models;

/// <summary>Session publishing to one game on mod.io.</summary>
public sealed class ModioSession : WorkshopSession
{
    /// <summary>mod.io game id.</summary>
    public uint GameId { get; set; }

    /// <summary>Game slug used in mod.io URLs (<c>mod.io/g/{slug}</c>).</summary>
    public string? GameNameId { get; set; }

    [JsonIgnore]
    public override WorkshopPlatform Platform => WorkshopPlatform.ModIo;

    public override bool IsSameGame(WorkshopSession other) => other is ModioSession modio && modio.GameId == GameId;

    [JsonIgnore]
    public override string GameIdDisplay => $"mod.io: {GameNameId ?? GameId.ToString()}";

    public override string ToString() => $"{GameName ?? Name} (mod.io game {GameId})";
}
