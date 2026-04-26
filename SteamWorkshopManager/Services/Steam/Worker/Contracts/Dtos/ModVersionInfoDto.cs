namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

public sealed record ModVersionInfoDto(uint VersionIndex, string BranchMin, string BranchMax);
