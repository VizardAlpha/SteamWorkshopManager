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
        Id = dto.PublishedFileId,
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
        OwnerId = dto.OwnerId,
        IsOwner = dto.IsOwner,
        AdditionalPreviews = dto.AdditionalPreviews.Select(ToModel).ToList(),
        Summary = dto.Summary,
        ProfileUrl = dto.ProfileUrl,
    };

    public static WorkshopItemDto ToDto(WorkshopItem item) => new(
        item.Id,
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
        item.OwnerId,
        item.IsOwner,
        item.AdditionalPreviews.Select(p => new WorkshopPreviewDto(
            (int)p.PreviewType, p.OriginalIndex, p.RemoteUrl, p.OriginalFilename)).ToList(),
        item.Summary,
        item.ProfileUrl);

    private static WorkshopPreview ToModel(WorkshopPreviewDto dto) => new()
    {
        Source = WorkshopPreviewSource.Existing,
        PreviewType = (EItemPreviewType)dto.PreviewType,
        OriginalIndex = dto.OriginalIndex,
        RemoteUrl = dto.RemoteUrl,
        OriginalFilename = dto.OriginalFilename,
    };
}
