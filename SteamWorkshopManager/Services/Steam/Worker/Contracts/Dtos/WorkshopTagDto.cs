namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// JSON-friendly mirror of <see cref="Models.WorkshopTag"/>. The <c>IsSelected</c>
/// flag is intentionally dropped because it's a UI/VM concern — the worker
/// only reports what Steam says.
/// </summary>
public sealed record WorkshopTagDto(string Name);
