using System.Collections.Generic;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// Payload for <c>ISteamWorker.CreateItemAsync</c>. Groups every create-item
/// parameter into a single DTO so the RPC signature stays flat and new fields
/// can land without breaking the interface shape.
/// </summary>
public sealed record CreateItemRequestDto(
    string Title,
    string Description,
    string ContentFolderPath,
    string? PreviewImagePath,
    VisibilityType Visibility,
    List<string> Tags,
    string? Changelog,
    string? BranchMin,
    string? BranchMax,
    List<PreviewOpDto>? PreviewOps
);