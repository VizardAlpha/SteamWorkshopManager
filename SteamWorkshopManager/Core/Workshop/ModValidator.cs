using System.IO;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>
/// Outcome of a pre-submit validation pass. <see cref="ErrorKey"/> matches a
/// localization key the caller resolves through <c>LocalizationService</c>.
/// </summary>
public sealed record ModValidationResult(bool IsValid, string? ErrorKey)
{
    public static ModValidationResult Ok { get; } = new(true, null);
    public static ModValidationResult Fail(string errorKey) => new(false, errorKey);
}

/// <summary>
/// Pure validation rules applied before a Workshop Create/Update is submitted
/// to Steam. Pulled out of the view-models so they stay thin and the rules
/// are testable without Avalonia.
/// </summary>
public static class ModValidator
{
    public const long MaxImageSizeBytes = 1024 * 1024;

    /// <summary>Validates the form state required for a fresh Workshop publish.</summary>
    public static ModValidationResult ValidateForCreate(string? title, string? contentFolderPath)
    {
        if (string.IsNullOrWhiteSpace(title)) return ModValidationResult.Fail("TitleRequired");
        if (string.IsNullOrWhiteSpace(contentFolderPath)) return ModValidationResult.Fail("FolderRequired");
        if (!Directory.Exists(contentFolderPath)) return ModValidationResult.Fail("FolderNotExist");
        return ModValidationResult.Ok;
    }

    /// <summary>Validates the form state for an existing item update — only
    /// title is mandatory; folder and preview are optional on update.</summary>
    public static ModValidationResult ValidateForUpdate(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return ModValidationResult.Fail("TitleRequired");
        return ModValidationResult.Ok;
    }

    /// <summary>True when the preview image at <paramref name="path"/> exceeds
    /// Steam's hard 1 MB limit. Used by the UI to surface a red-state badge.</summary>
    public static bool IsImageTooLarge(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        try { return new FileInfo(path).Length > MaxImageSizeBytes; }
        catch { return false; }
    }
}
