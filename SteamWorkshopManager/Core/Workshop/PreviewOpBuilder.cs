using System.Collections.Generic;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Core.Workshop;

/// <summary>
/// Builds the <see cref="PreviewOp"/> list submitted with a Workshop update.
/// The "everything is new" path (Create) lives here; the editor's diff-based
/// rebuild keeps its own logic since it depends on local Steam-side state.
/// </summary>
public static class PreviewOpBuilder
{
    /// <summary>Builds an op list for a fresh Create flow where every entry
    /// is a brand-new image or YouTube video.</summary>
    public static List<PreviewOp> BuildForCreate(
        IEnumerable<WorkshopPreview> imagePreviews,
        IEnumerable<WorkshopPreview> videoPreviews)
    {
        var ops = new List<PreviewOp>();
        foreach (var p in imagePreviews) AppendNewPreviewOp(ops, p);
        foreach (var p in videoPreviews) AppendNewPreviewOp(ops, p);
        return ops;
    }

    /// <summary>
    /// Common emit point for a preview entry the user has just added. Picks
    /// the right Steam UGC entrypoint based on the preview source.
    /// </summary>
    public static void AppendNewPreviewOp(List<PreviewOp> ops, WorkshopPreview p)
    {
        switch (p.Source)
        {
            case WorkshopPreviewSource.NewImage when !string.IsNullOrEmpty(p.LocalPath):
                ops.Add(new PreviewOp.AddImage(p.LocalPath));
                break;
            case WorkshopPreviewSource.NewVideo when !string.IsNullOrEmpty(p.VideoId):
                ops.Add(new PreviewOp.AddVideo(p.VideoId));
                break;
        }
    }
}
