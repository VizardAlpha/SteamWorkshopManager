namespace SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

/// <summary>Discriminator for <see cref="PreviewOpDto"/>. Flat int instead
/// of an abstract-record union so System.Text.Json doesn't need polymorphic
/// configuration on the RPC channel.</summary>
public enum PreviewOpKind
{
    Remove = 0,
    AddImage = 1,
    AddVideo = 2,
}

/// <summary>
/// Wire-format mutation applied to a Workshop item's additional-previews list
/// during an update RPC. Only the field matching <see cref="Kind"/> is read
/// on the worker side.
/// </summary>
public sealed record PreviewOpDto(
    PreviewOpKind Kind,
    uint? RemoveIndex,
    string? FilePath,
    string? YouTubeId
);