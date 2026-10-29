using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SteamWorkshopManager.Services.Platforms.Modio;

// Wire DTOs for the mod.io REST API (snake_case, see ModioJsonContext).
// Read leniently: mod.io may add fields we don't model.

public sealed class ModioErrorResponse
{
    public ModioError? Error { get; set; }
}

/// <summary><see cref="ErrorRef"/> is the stable code to switch on; <see cref="Errors"/> holds field errors on HTTP 422.</summary>
public sealed class ModioError
{
    public int Code { get; set; }
    public int ErrorRef { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, string>? Errors { get; set; }
}

public sealed class ModioPaged<T>
{
    public List<T> Data { get; set; } = [];
    public int ResultCount { get; set; }
    public int ResultOffset { get; set; }
    public int ResultLimit { get; set; }
    public int ResultTotal { get; set; }
}

public sealed class ModioUser
{
    public long Id { get; set; }
    public string? NameId { get; set; }
    public string? Username { get; set; }
    public string? ProfileUrl { get; set; }
    public ModioAvatar? Avatar { get; set; }
}

public sealed class ModioAvatar
{
    public string? Original { get; set; }
    [JsonPropertyName("thumb_50x50")] public string? Thumb50X50 { get; set; }
    [JsonPropertyName("thumb_100x100")] public string? Thumb100X100 { get; set; }
}

public sealed class ModioGame
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameId { get; set; }
    public string? ProfileUrl { get; set; }
    public ModioLogo? Logo { get; set; }
    public ModioIcon? Icon { get; set; }

    /// <summary>0 = no dependencies, 1 = opt-in, 2 = opt-out, 3 = no restrictions.</summary>
    public int DependencyOption { get; set; }
}

public sealed class ModioMod
{
    public long Id { get; set; }
    public long GameId { get; set; }

    /// <summary>0 = not accepted, 1 = accepted, 3 = deleted.</summary>
    public int Status { get; set; }

    /// <summary>0 = hidden, 1 = public.</summary>
    public int Visible { get; set; }

    public string Name { get; set; } = "";
    public string? NameId { get; set; }
    public string? Summary { get; set; }

    /// <summary>HTML.</summary>
    public string? Description { get; set; }

    public string? DescriptionPlaintext { get; set; }
    public string? ProfileUrl { get; set; }
    public string? HomepageUrl { get; set; }
    public ModioLogo? Logo { get; set; }
    public ModioModfile? Modfile { get; set; }
    public ModioMedia? Media { get; set; }

    /// <summary>Bit field: 1 = comments, 64 = previews, 128 = preview URL, 1024 = dependencies, 131072 = collections.</summary>
    public int CommunityOptions { get; set; }

    public List<ModioModTag> Tags { get; set; } = [];
    public ModioModStats? Stats { get; set; }
    public ModioUser? SubmittedBy { get; set; }
    public long DateAdded { get; set; }
    public long DateUpdated { get; set; }
    public long DateLive { get; set; }
}

public sealed class ModioModfile
{
    public long Id { get; set; }
    public long ModId { get; set; }
    public long Filesize { get; set; }
    public string? Filename { get; set; }
    public string? Version { get; set; }
    public string? Changelog { get; set; }
    public long DateAdded { get; set; }

    /// <summary>0 = not scanned, 1 = scanned, 2 = scanning, 3 = too large, 4 = not found, 5 = error.</summary>
    public int VirusStatus { get; set; }

    /// <summary>1 when the scan flagged the file.</summary>
    public int VirusPositive { get; set; }

    public ModioFilehash? Filehash { get; set; }
    public ModioDownload? Download { get; set; }
}

public sealed class ModioFilehash
{
    public string? Md5 { get; set; }
}

/// <summary>The link may carry a short-lived hash: ask for a fresh one before each download.</summary>
public sealed class ModioDownload
{
    public string? BinaryUrl { get; set; }
    public long DateExpires { get; set; }
}

public sealed class ModioLogo
{
    public string? Filename { get; set; }
    public string? Original { get; set; }
    [JsonPropertyName("thumb_320x180")] public string? Thumb320X180 { get; set; }
    [JsonPropertyName("thumb_640x360")] public string? Thumb640X360 { get; set; }
    [JsonPropertyName("thumb_1280x720")] public string? Thumb1280X720 { get; set; }
}

public sealed class ModioIcon
{
    public string? Original { get; set; }
    [JsonPropertyName("thumb_64x64")] public string? Thumb64X64 { get; set; }
    [JsonPropertyName("thumb_128x128")] public string? Thumb128X128 { get; set; }
}

/// <summary>Gallery of a mod. Images are deleted by file name, videos and models by URL.</summary>
public sealed class ModioMedia
{
    public List<ModioImage> Images { get; set; } = [];
    public List<string> Youtube { get; set; } = [];
    public List<string> Sketchfab { get; set; } = [];
}

public sealed class ModioImage
{
    public string? Filename { get; set; }
    public string? Original { get; set; }
    [JsonPropertyName("thumb_320x180")] public string? Thumb320X180 { get; set; }
}

/// <summary>A mod the edited mod needs; <see cref="DependencyDepth"/> is 0 for direct ones.</summary>
public sealed class ModioModDependency
{
    public long ModId { get; set; }
    public string? Name { get; set; }
    public string? NameId { get; set; }
    public string? ProfileUrl { get; set; }
    public ModioLogo? Logo { get; set; }
    public int DependencyDepth { get; set; }
}

public sealed class ModioModTag
{
    public string Name { get; set; } = "";
}

public sealed class ModioModStats
{
    public long SubscribersTotal { get; set; }
    public long DownloadsTotal { get; set; }
}

/// <summary>One tag group. Hidden and locked groups aren't for authors to pick.</summary>
public sealed class ModioTagOption
{
    public string Name { get; set; } = "";

    /// <summary>"checkboxes" (multi-select) or "dropdown" (single-select).</summary>
    public string? Type { get; set; }

    public List<string> Tags { get; set; } = [];
    public bool Hidden { get; set; }
    public bool Locked { get; set; }
}

public sealed class ModioMultipartUpload
{
    public string? UploadId { get; set; }
    public int Status { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ModioErrorResponse))]
[JsonSerializable(typeof(ModioUser))]
[JsonSerializable(typeof(ModioGame))]
[JsonSerializable(typeof(ModioMod))]
[JsonSerializable(typeof(ModioModfile))]
[JsonSerializable(typeof(ModioMultipartUpload))]
[JsonSerializable(typeof(ModioPaged<ModioGame>), TypeInfoPropertyName = "PagedGames")]
[JsonSerializable(typeof(ModioPaged<ModioMod>), TypeInfoPropertyName = "PagedMods")]
[JsonSerializable(typeof(ModioPaged<ModioTagOption>), TypeInfoPropertyName = "PagedTagOptions")]
[JsonSerializable(typeof(ModioPaged<ModioModfile>), TypeInfoPropertyName = "PagedModfiles")]
[JsonSerializable(typeof(List<ModioModfile>))]
[JsonSerializable(typeof(List<ModioModDependency>))]
[JsonSerializable(typeof(ModioPaged<ModioModDependency>), TypeInfoPropertyName = "PagedDependencies")]
internal partial class ModioJsonContext : JsonSerializerContext;
