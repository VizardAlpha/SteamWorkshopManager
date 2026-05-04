namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>
/// JSON-friendly mirror of an additional preview entry (image or YouTube
/// video) attached to a Workshop item. <c>PreviewType</c> is transported as
/// the raw <c>EItemPreviewType</c> int so the contract stays free of the
/// Steamworks namespace.
/// </summary>
public sealed record WorkshopPreviewDto(
    int PreviewType,
    uint OriginalIndex,
    string? RemoteUrl,
    string? OriginalFilename
);