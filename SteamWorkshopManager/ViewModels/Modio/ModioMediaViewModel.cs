using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Platforms.Modio;
using SteamWorkshopManager.Services.UI;

namespace SteamWorkshopManager.ViewModels.Modio;

/// <summary>One gallery image: already on mod.io (removed by file name) or picked here (uploaded from its path).</summary>
public partial class ModioGalleryImage : ObservableObject, IDisposable
{
    [ObservableProperty]
    private Bitmap? _thumbnail;

    partial void OnThumbnailChanging(Bitmap? value) => _thumbnail?.Dispose();

    public string? RemoteFileName { get; init; }
    public string? LocalPath { get; init; }
    public bool IsNew => LocalPath is not null;
    public string Label => LocalPath is { } path ? Path.GetFileName(path) : RemoteFileName ?? string.Empty;

    public void Dispose() => Thumbnail = null;
}

/// <summary>Media step of a mod.io mod: gallery images, YouTube videos and Sketchfab models.</summary>
public partial class ModioMediaViewModel : ViewModelBase, IDisposable
{
    private readonly IFileDialogService _fileDialog;
    private readonly CancellationTokenSource _cts = new();

    private HashSet<string> _savedImages = [];
    private HashSet<string> _savedYoutube = [];
    private HashSet<string> _savedSketchfab = [];

    [ObservableProperty]
    private string _newYoutubeUrl = string.Empty;

    [ObservableProperty]
    private string _newSketchfabUrl = string.Empty;

    /// <summary>Last refused input (wrong link, unusable image), cleared by the next valid one.</summary>
    [ObservableProperty]
    private string? _error;

    public ObservableCollection<ModioGalleryImage> Images { get; } = [];
    public ObservableCollection<string> YoutubeUrls { get; } = [];
    public ObservableCollection<string> SketchfabUrls { get; } = [];

    public bool HasImages => Images.Count > 0;

    public ModioMediaViewModel(IFileDialogService fileDialog)
    {
        _fileDialog = fileDialog;
        Images.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasImages));
    }

    /// <summary>Replaces the lists with what mod.io holds now.</summary>
    public void LoadExisting(ModioMedia? media, ThumbnailCache thumbnails)
    {
        foreach (var image in Images) image.Dispose();
        Images.Clear();
        YoutubeUrls.Clear();
        SketchfabUrls.Clear();

        foreach (var remote in media?.Images ?? [])
        {
            if (remote.Filename is null) continue;
            var image = new ModioGalleryImage { RemoteFileName = remote.Filename };
            Images.Add(image);
            if ((remote.Thumb320X180 ?? remote.Original) is { } url) _ = LoadThumbnailAsync(image, thumbnails, url);
        }
        foreach (var url in media?.Youtube ?? []) YoutubeUrls.Add(url);
        foreach (var url in media?.Sketchfab ?? []) SketchfabUrls.Add(url);

        _savedImages = Images.Select(i => i.RemoteFileName!).ToHashSet();
        _savedYoutube = YoutubeUrls.ToHashSet();
        _savedSketchfab = SketchfabUrls.ToHashSet();
    }

    private async Task LoadThumbnailAsync(ModioGalleryImage image, ThumbnailCache thumbnails, string url)
    {
        var bitmap = await thumbnails.GetAsync(url, _cts.Token);
        if (bitmap is null || _cts.IsCancellationRequested || !Images.Contains(image)) bitmap?.Dispose();
        else image.Thumbnail = bitmap;
    }

    public bool HasChanges => BuildChange() is { } c && (c.HasAdditions || c.HasRemovals);

    public ModioMediaChange BuildChange() => new(
        Images.Where(i => i.IsNew).Select(i => i.LocalPath!).ToList(),
        YoutubeUrls.Where(u => !_savedYoutube.Contains(u)).ToList(),
        SketchfabUrls.Where(u => !_savedSketchfab.Contains(u)).ToList(),
        _savedImages.Where(name => Images.All(i => i.RemoteFileName != name)).ToList(),
        _savedYoutube.Where(u => !YoutubeUrls.Contains(u)).ToList(),
        _savedSketchfab.Where(u => !SketchfabUrls.Contains(u)).ToList());

    [RelayCommand]
    private async Task AddImagesAsync()
    {
        var paths = await _fileDialog.OpenFilesAsync(Loc["ModioAddImages"], ModioModRules.ImageExtensions);
        AddImages(paths);
    }

    [RelayCommand]
    private void ImagesDropped(DropPayload payload) => AddImages(payload.Paths);

    private void AddImages(IEnumerable<string> paths)
    {
        Error = null;
        foreach (var path in paths)
        {
            if (Images.Any(i => string.Equals(i.LocalPath, path, StringComparison.OrdinalIgnoreCase))) continue;
            if (ModioModRules.ValidateImage(path) is { } key)
            {
                Error = $"{Path.GetFileName(path)}: {Loc[key]}";
                continue;
            }

            var image = new ModioGalleryImage { LocalPath = path };
            try { image.Thumbnail = new Bitmap(path); }
            catch { /* shown without thumbnail; mod.io will say if it can't read it */ }
            Images.Add(image);
        }
    }

    [RelayCommand]
    private void RemoveImage(ModioGalleryImage image)
    {
        image.Dispose();
        Images.Remove(image);
    }

    [RelayCommand]
    private void AddYoutube()
    {
        var url = NewYoutubeUrl.Trim();
        if (url.Length == 0) return;
        if (!IsLink(url, "youtube.com", "youtu.be"))
        {
            Error = Loc["ModioYoutubeInvalid"];
            return;
        }
        Error = null;
        if (!YoutubeUrls.Contains(url)) YoutubeUrls.Add(url);
        NewYoutubeUrl = string.Empty;
    }

    [RelayCommand]
    private void RemoveYoutube(string url) => YoutubeUrls.Remove(url);

    [RelayCommand]
    private void AddSketchfab()
    {
        var url = NewSketchfabUrl.Trim();
        if (url.Length == 0) return;
        if (!IsLink(url, "sketchfab.com"))
        {
            Error = Loc["ModioSketchfabInvalid"];
            return;
        }
        Error = null;
        if (!SketchfabUrls.Contains(url)) SketchfabUrls.Add(url);
        NewSketchfabUrl = string.Empty;
    }

    [RelayCommand]
    private void RemoveSketchfab(string url) => SketchfabUrls.Remove(url);

    private static bool IsLink(string url, params string[] hosts) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && hosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase)
                          || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        foreach (var image in Images) image.Dispose();
    }
}
