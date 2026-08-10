namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// Result of <c>ISteamWorker.CreateItemAsync</c>. <see cref="FileId"/> is 0 when
/// creation failed. <see cref="ErrorCode"/> is the raw Steam <c>EResult</c> that
/// caused it (0 when Steam reported none), rehydrated by the shell-side proxy so
/// the mapping to a user-facing message stays in one place.
/// </summary>
public record CreateItemResultDto(ulong FileId, int ErrorCode);