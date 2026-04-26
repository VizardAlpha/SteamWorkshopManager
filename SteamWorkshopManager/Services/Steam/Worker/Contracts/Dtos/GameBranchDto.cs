namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// JSON-friendly mirror of <see cref="Models.GameBranch"/>. Raw flags bits are
/// transported as-is; the computed <c>IsDefault</c> / <c>IsAvailable</c> /
/// <c>IsPrivate</c> helpers are derived on the shell side after rehydration.
/// </summary>
public sealed record GameBranchDto(
    string Name,
    string Description,
    uint BuildId,
    uint Flags
);
