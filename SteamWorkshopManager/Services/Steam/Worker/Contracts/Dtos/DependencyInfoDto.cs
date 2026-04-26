namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// JSON-friendly mirror of <see cref="Models.DependencyInfo"/>. UI-only state
/// (IsRemoving, WorkshopUrl computed property) stays on the shell side when
/// the DTO is mapped to the domain model.
/// </summary>
public sealed record DependencyInfoDto(
    ulong PublishedFileId,
    string Title,
    string PreviewUrl,
    bool IsValid
);
