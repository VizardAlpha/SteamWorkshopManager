using System.Linq;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;
using Steamworks;

namespace SteamWorkshopManager.Services.Steam;

/// <summary>Converts items between the wire/cache DTO and the shell model.</summary>
internal static class WorkshopItemMapper
{
    public static WorkshopItem ToModel(WorkshopItemDto dto) => new()
    {
        PublishedFileId = new PublishedFileId_t(dto.PublishedFileId),
        Title = dto.Title,
        Description = dto.Description,
        PreviewImagePath = dto.PreviewImagePath,
        PreviewImageUrl = dto.PreviewImageUrl,
        Visibility = dto.Visibility,
        Tags = dto.Tags.Select(t => new WorkshopTag(t.Name)).ToList(),
        CreatedAt = dto.CreatedAt,
        UpdatedAt = dto.UpdatedAt,
        SubscriberCount = dto.SubscriberCount,
        FileSize = dto.FileSize,
        OwnerId = new CSteamID(dto.OwnerId),
        IsOwner = dto.IsOwner,
        AdditionalPreviews = dto.AdditionalPreviews.Select(ToModel).ToList(),
    };

    public static WorkshopItemDto ToDto(WorkshopItem item) => new(
        (ulong)item.PublishedFileId,
        item.Title,
        item.Description,
        item.PreviewImagePath,
        item.PreviewImageUrl,
        item.Visibility,
        item.Tags.Select(t => new WorkshopTagDto(t.Name)).ToList(),
        item.CreatedAt,
        item.UpdatedAt,
        item.SubscriberCount,
        item.FileSize,
        (ulong)item.OwnerId,
        item.IsOwner,
        item.AdditionalPreviews.Select(p => new WorkshopPreviewDto(
            (int)p.PreviewType, p.OriginalIndex, p.RemoteUrl, p.OriginalFilename)).ToList());

    private static WorkshopPreview ToModel(WorkshopPreviewDto dto) => new()
    {
        Source = WorkshopPreviewSource.Existing,
        PreviewType = (EItemPreviewType)dto.PreviewType,
        OriginalIndex = dto.OriginalIndex,
        RemoteUrl = dto.RemoteUrl,
        OriginalFilename = dto.OriginalFilename,
    };
}
