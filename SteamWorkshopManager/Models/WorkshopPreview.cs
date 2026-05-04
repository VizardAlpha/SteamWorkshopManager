using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Steamworks;

namespace SteamWorkshopManager.Models;

/// <summary>
/// Origin of a preview entry in the editor list. Drives both the save-time
/// diff (only Existing entries can be removed by index; New ones must be
/// added via Add*) and the UI affordances (preview thumbnail vs. local file
/// path label).
/// </summary>
public enum WorkshopPreviewSource
{
    /// <summary>Preview already published on Steam — has OriginalIndex.</summary>
    Existing,
    /// <summary>Local image file pending upload on next save.</summary>
    NewImage,
    /// <summary>YouTube video pending submit on next save.</summary>
    NewVideo,
}

/// <summary>
/// A single entry in the additional-previews carousel of a Workshop item.
/// Steam exposes this as a heterogeneous list of images and videos managed
/// independently from the main thumbnail.
/// </summary>
public partial class WorkshopPreview : ObservableObject
{
    public required WorkshopPreviewSource Source { get; init; }

    /// <summary>k_EItemPreviewType_Image / _YouTubeVideo / _Sketchfab / _EnvironmentMap_*.</summary>
    public required EItemPreviewType PreviewType { get; init; }

    /// <summary>Steam-side index when Source == Existing. Required for RemoveItemPreview.</summary>
    public uint OriginalIndex { get; init; }

    /// <summary>CDN URL (image) or YouTube video ID, depending on PreviewType.</summary>
    public string? RemoteUrl { get; init; }

    /// <summary>Original filename reported by Steam for existing image previews.</summary>
    public string? OriginalFilename { get; init; }

    /// <summary>Local path for NewImage entries pending upload.</summary>
    public string? LocalPath { get; init; }

    /// <summary>YouTube ID for NewVideo entries pending upload.</summary>
    public string? VideoId { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldShowImagePlaceholder))]
    private Bitmap? _thumbnail;

    [ObservableProperty] private bool _isRemoving;

    public bool IsVideo => PreviewType == EItemPreviewType.k_EItemPreviewType_YouTubeVideo;
    public bool IsImage => PreviewType == EItemPreviewType.k_EItemPreviewType_Image;
    public bool IsSketchfab => PreviewType == EItemPreviewType.k_EItemPreviewType_Sketchfab;
    public bool IsNew => Source != WorkshopPreviewSource.Existing;

    /// <summary>
    /// Show the dimmed placeholder icon only when the entry is an image AND
    /// no bitmap has been loaded yet — embedded media types (YouTube /
    /// Sketchfab) render their own icon and short-circuit this.
    /// </summary>
    public bool ShouldShowImagePlaceholder => IsImage && Thumbnail == null;

    public string DisplayLabel => Source switch
    {
        WorkshopPreviewSource.NewImage => Path.GetFileName(LocalPath ?? "(image)"),
        WorkshopPreviewSource.NewVideo when IsSketchfab => $"Sketchfab: {VideoId}",
        WorkshopPreviewSource.NewVideo => $"YouTube: {VideoId}",
        _ when IsSketchfab => $"Sketchfab: {RemoteUrl}",
        _ when IsVideo => $"YouTube: {RemoteUrl}",
        _ => OriginalFilename ?? Path.GetFileName(RemoteUrl ?? "(image)"),
    };
}