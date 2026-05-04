using System;
using System.Collections.Generic;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// JSON-friendly mirror of <see cref="WorkshopItem"/> for the worker RPC.
/// The UI-only <c>PreviewBitmap</c> is intentionally absent — image loading
/// stays on the shell so bitmaps never cross the process boundary.
/// Steamworks struct types (<c>PublishedFileId_t</c>, <c>CSteamID</c>) are
/// transported as raw <see cref="ulong"/>.
/// </summary>
public sealed record WorkshopItemDto(
    ulong PublishedFileId,
    string Title,
    string Description,
    string? PreviewImagePath,
    string? PreviewImageUrl,
    VisibilityType Visibility,
    List<WorkshopTagDto> Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    ulong SubscriberCount,
    long FileSize,
    ulong OwnerId,
    bool IsOwner,
    List<WorkshopPreviewDto> AdditionalPreviews
);
