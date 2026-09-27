using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Core.Workshop;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.UI;
using Steamworks;

namespace SteamWorkshopManager.ViewModels.Editor;

/// <summary>
/// Additional previews (images, YouTube videos, read-only Sketchfab models) shared by
/// the create and edit views, and the translation of the user's edits into preview ops.
/// </summary>
public partial class PreviewGalleryViewModel(IFileDialogService fileDialogService) : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<PreviewGalleryViewModel>();
    private static readonly HttpClient Http = SteamHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(30));

    /// <summary>Carousel sub-lists, shown by Steam as separate sections: reorder is per list.</summary>
    public ObservableCollection<WorkshopPreview> ImagePreviews { get; } = [];
    public ObservableCollection<WorkshopPreview> VideoPreviews { get; } = [];
    public ObservableCollection<WorkshopPreview> ModelPreviews { get; } = [];

    // Steam-side indices captured at load: a reorder rebuild removes all of them.
    private readonly List<uint> _originalExistingIndices = [];
    private readonly List<uint> _removedExistingIndices = [];
    private string? _tempDir;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYouTubeInputValid))]
    private string _newYouTubeInput = string.Empty;

    [ObservableProperty]
    private string? _previewError;

    public bool IsYouTubeInputValid => !string.IsNullOrWhiteSpace(WorkshopInputParser.ParseYouTubeId(NewYouTubeInput));

    /// <summary>Loads the previews an existing item already has on Steam.</summary>
    public void LoadExisting(IEnumerable<WorkshopPreview> previews)
    {
        foreach (var preview in previews)
        {
            ListFor(preview).Add(preview);
            _originalExistingIndices.Add(preview.OriginalIndex);
            if (preview.IsImage && !string.IsNullOrEmpty(preview.RemoteUrl))
                _ = LoadThumbnailAsync(preview);
        }
    }

    [RelayCommand]
    private async Task AddImagePreviewAsync()
    {
        PreviewError = null;
        var paths = await fileDialogService.OpenFilesAsync(Loc["SelectPreviewImage"], WorkshopMedia.ImageExtensions);
        foreach (var path in paths) AddImageFromPath(path);
    }

    [RelayCommand]
    private void PreviewImagesDropped(DropPayload payload)
    {
        PreviewError = null;
        foreach (var path in payload.Paths) AddImageFromPath(path);
    }

    private void AddImageFromPath(string path)
    {
        if (ImagePreviews.Any(p => string.Equals(p.LocalPath, path, StringComparison.OrdinalIgnoreCase)))
            return;

        var preview = new WorkshopPreview
        {
            Source = WorkshopPreviewSource.NewImage,
            PreviewType = EItemPreviewType.k_EItemPreviewType_Image,
            LocalPath = path,
        };
        try { preview.Thumbnail = new Bitmap(path); } catch { /* unreadable image: keep the entry without thumbnail */ }
        ImagePreviews.Add(preview);
    }

    [RelayCommand]
    private void AddYouTubeVideo()
    {
        PreviewError = null;
        var id = WorkshopInputParser.ParseYouTubeId(NewYouTubeInput);
        if (string.IsNullOrEmpty(id))
        {
            PreviewError = Loc["InvalidYouTubeInput"];
            return;
        }
        if (VideoPreviews.Any(p => p.VideoId == id || p.RemoteUrl == id))
        {
            PreviewError = Loc["YouTubeAlreadyAdded"];
            return;
        }

        VideoPreviews.Add(new WorkshopPreview
        {
            Source = WorkshopPreviewSource.NewVideo,
            PreviewType = EItemPreviewType.k_EItemPreviewType_YouTubeVideo,
            VideoId = id,
        });
        NewYouTubeInput = string.Empty;
    }

    [RelayCommand]
    private void RemovePreview(WorkshopPreview? preview)
    {
        if (preview == null) return;
        if (preview.Source == WorkshopPreviewSource.Existing)
            _removedExistingIndices.Add(preview.OriginalIndex);

        preview.Thumbnail?.Dispose();
        FindContainingList(preview)?.Remove(preview);
    }

    [RelayCommand]
    private void MovePreviewUp(WorkshopPreview preview)
    {
        var list = FindContainingList(preview);
        if (list == null) return;
        var index = list.IndexOf(preview);
        if (index > 0) list.Move(index, index - 1);
    }

    [RelayCommand]
    private void MovePreviewDown(WorkshopPreview preview)
    {
        var list = FindContainingList(preview);
        if (list == null) return;
        var index = list.IndexOf(preview);
        if (index >= 0 && index < list.Count - 1) list.Move(index, index + 1);
    }

    /// <summary>Drag reorder within one carousel list. Models are read-only.</summary>
    [RelayCommand]
    private void ReorderPreview(ReorderRequest request)
    {
        if (request.Source is not WorkshopPreview source || request.Target is not WorkshopPreview target) return;
        if (source.IsSketchfab || target.IsSketchfab) return;

        var list = FindContainingList(source);
        if (list is null || !ReferenceEquals(list, FindContainingList(target))) return;

        ListReorder.Move(list, source, target);
    }

    public List<PreviewOp> BuildOpsForCreate() => PreviewOpBuilder.BuildForCreate(ImagePreviews, VideoPreviews);

    /// <summary>
    /// Ops for an update. Fast path when entries were only added or removed; a full
    /// rebuild (re-downloading kept images) when the order changed, since Steam has no
    /// "move". Returns null when a kept image can't be downloaded, so the save aborts.
    /// Call <see cref="CleanupTempFiles"/> once the upload is done.
    /// </summary>
    public async Task<List<PreviewOp>?> BuildOpsForUpdateAsync()
    {
        if (!IsListOutOfOrder(ImagePreviews) && !IsListOutOfOrder(VideoPreviews))
        {
            var ops = new List<PreviewOp>();
            foreach (var idx in _removedExistingIndices) ops.Add(new PreviewOp.Remove(idx));
            foreach (var p in ImagePreviews) PreviewOpBuilder.AppendNewPreviewOp(ops, p);
            foreach (var p in VideoPreviews) PreviewOpBuilder.AppendNewPreviewOp(ops, p);
            return ops;
        }
        return await BuildRebuildOpsAsync();
    }

    public void CleanupTempFiles()
    {
        if (_tempDir is null) return;
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not delete preview temp dir: {ex.Message}");
        }
        _tempDir = null;
    }

    private async Task<List<PreviewOp>?> BuildRebuildOpsAsync()
    {
        var ops = new List<PreviewOp>();

        // Kept Sketchfabs stay untouched: the SDK can't re-add them.
        var preserve = ModelPreviews
            .Where(m => m.Source == WorkshopPreviewSource.Existing)
            .Select(m => m.OriginalIndex)
            .ToHashSet();

        foreach (var idx in _originalExistingIndices)
        {
            if (!preserve.Contains(idx)) ops.Add(new PreviewOp.Remove(idx));
        }

        _tempDir = AppPaths.TempPreviewDir();
        Directory.CreateDirectory(_tempDir);

        foreach (var p in ImagePreviews)
        {
            var path = p.LocalPath;
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(p.RemoteUrl))
            {
                // Every existing image is removed first: a missing re-add would delete it for good.
                path = await DownloadToTempAsync(p.RemoteUrl, _tempDir);
                if (path is null) return null;
            }
            if (!string.IsNullOrEmpty(path)) ops.Add(new PreviewOp.AddImage(path));
        }

        foreach (var p in VideoPreviews)
        {
            var id = p.Source == WorkshopPreviewSource.NewVideo ? p.VideoId : p.RemoteUrl;
            if (!string.IsNullOrEmpty(id)) ops.Add(new PreviewOp.AddVideo(id));
        }

        return ops;
    }

    /// <summary>
    /// A list is in order when existing entries keep their ascending Steam index
    /// and new entries only come after them.
    /// </summary>
    internal static bool IsListOutOfOrder(IEnumerable<WorkshopPreview> list)
    {
        var seenNew = false;
        uint? lastIdx = null;
        foreach (var p in list)
        {
            if (p.Source == WorkshopPreviewSource.Existing)
            {
                if (seenNew) return true;
                if (lastIdx is { } prev && p.OriginalIndex < prev) return true;
                lastIdx = p.OriginalIndex;
            }
            else
            {
                seenNew = true;
            }
        }
        return false;
    }

    private ObservableCollection<WorkshopPreview> ListFor(WorkshopPreview p) =>
        p.IsVideo ? VideoPreviews : p.IsSketchfab ? ModelPreviews : ImagePreviews;

    private ObservableCollection<WorkshopPreview>? FindContainingList(WorkshopPreview p)
    {
        if (ImagePreviews.Contains(p)) return ImagePreviews;
        if (VideoPreviews.Contains(p)) return VideoPreviews;
        if (ModelPreviews.Contains(p)) return ModelPreviews;
        return null;
    }

    private static async Task LoadThumbnailAsync(WorkshopPreview preview)
    {
        var bitmap = await ThumbnailCache.GetAsync(preview.RemoteUrl!, CancellationToken.None);
        if (bitmap is not null) preview.Thumbnail = bitmap;
    }

    private static async Task<string?> DownloadToTempAsync(string url, string tempDir)
    {
        try
        {
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning($"Preview download failed ({(int)response.StatusCode}): {url}");
                return null;
            }

            // Steam CDN URLs usually end in .png/.jpg; fall back to .jpg otherwise.
            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";

            var path = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ext);
            await using var fs = File.Create(path);
            await response.Content.CopyToAsync(fs);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warning($"Preview download failed: {url}: {ex.Message}");
            return null;
        }
    }
}
