using System.Collections.Generic;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// Payload for <c>ISteamWorker.UpdateItemAsync</c>. Every nullable field is
/// "unchanged when null" — matches the semantics of the underlying
/// <see cref="Steamworks.SteamUGC"/> update handle.
/// </summary>
public sealed record UpdateItemRequestDto(
    ulong PublishedFileId,
    string? Title,
    string? Description,
    string? ContentFolderPath,
    string? PreviewImagePath,
    VisibilityType? Visibility,
    List<string>? Tags,
    string? Changelog,
    string? BranchMin,
    string? BranchMax,
    List<PreviewOpDto>? PreviewOps
);