using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Platforms.Modio;
using SteamWorkshopManager.Services.UI;
using SteamWorkshopManager.ViewModels.Editor;

namespace SteamWorkshopManager.ViewModels.Modio;

/// <summary>Details step of a mod.io mod: what mod.io's own form asks before the mod exists.</summary>
public partial class ModioDetailsViewModel : ViewModelBase, IDisposable
{
    private readonly IFileDialogService _fileDialog;
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryCounter), nameof(IsSummaryTooLong), nameof(IsComplete))]
    private string _summary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionCounter), nameof(IsDescriptionTooLong))]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _homepageUrl = string.Empty;

    [ObservableProperty]
    private bool _allowComments = true;

    [ObservableProperty]
    private bool _allowCollections = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPublic))]
    private VisibilityType _visibility = VisibilityType.Public;

    [ObservableProperty]
    private Bitmap? _logo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete), nameof(HasLogoPath))]
    private string? _logoPath;

    /// <summary>Why the picked logo can't be sent, null when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    private string? _logoError;

    [ObservableProperty]
    private string _logoSize = string.Empty;

    /// <summary>Fields stay readable but frozen once the create flow sent them.</summary>
    [ObservableProperty]
    private bool _isLocked;

    partial void OnLogoChanging(Bitmap? value) => _logo?.Dispose();

    public TagEditorViewModel Tags { get; }

    /// <summary>Create mode: mod.io requires a logo.</summary>
    public bool IsCreating { get; set; }

    public string SummaryCounter => $"{Summary.Length} / {ModioModRules.SummaryMaxLength}";

    public bool IsSummaryTooLong => Summary.Length > ModioModRules.SummaryMaxLength;

    public string DescriptionCounter => $"{Description.Length:N0} / {ModioModRules.DescriptionMaxLength:N0}";

    public bool IsDescriptionTooLong => Description.Length > ModioModRules.DescriptionMaxLength;

    public bool HasLogoPath => !string.IsNullOrEmpty(LogoPath);

    /// <summary>mod.io mods are public or hidden.</summary>
    public bool IsPublic
    {
        get => Visibility == VisibilityType.Public;
        set => Visibility = value ? VisibilityType.Public : VisibilityType.Private;
    }

    /// <summary>Left-nav check: required fields filled and the logo usable.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Title)
        && !string.IsNullOrWhiteSpace(Summary)
        && !IsSummaryTooLong
        && LogoError is null
        && (!IsCreating || HasLogoPath);

    public ModioDetailsViewModel(IFileDialogService fileDialog, TagEditorViewModel tags)
    {
        _fileDialog = fileDialog;
        Tags = tags;
    }

    /// <summary>Community option bits to send, keeping the ones this form doesn't show.</summary>
    public int CommunityOptions(int current)
    {
        current = ModioCommunity.With(current, ModioCommunity.Comments, AllowComments);
        return ModioCommunity.With(current, ModioCommunity.Collections, AllowCollections);
    }

    public void LoadCommunityOptions(int options)
    {
        AllowComments = (options & ModioCommunity.Comments) != 0;
        AllowCollections = (options & ModioCommunity.Collections) != 0;
    }

    /// <summary>Localization key of the first problem, null when the step can be sent.</summary>
    public string? Validate()
    {
        if (ModioModRules.Validate(Title, Summary, Description, HomepageUrl) is { } key) return key;
        if (LogoError is not null) return "ModioLogoInvalid";
        return IsCreating && !HasLogoPath ? "ModioLogoRequired" : null;
    }

    /// <summary>Shows the mod's current logo until the user picks another one.</summary>
    public async Task LoadRemoteLogoAsync(ThumbnailCache thumbnails, string url)
    {
        var bitmap = await thumbnails.GetAsync(url, _cts.Token);
        if (bitmap is null || LogoPath is not null || _cts.IsCancellationRequested) bitmap?.Dispose();
        else Logo = bitmap;
    }

    /// <summary>After a save the picked logo is on mod.io.</summary>
    public void ClearPendingLogo()
    {
        LogoPath = null;
        LogoSize = string.Empty;
    }

    [RelayCommand]
    private async Task BrowseLogoAsync()
    {
        var path = await _fileDialog.OpenFileAsync(Loc["ModioSelectLogo"], ModioModRules.ImageExtensions);
        if (!string.IsNullOrEmpty(path)) SetLogo(path);
    }

    [RelayCommand]
    private void LogoDropped(DropPayload payload)
    {
        if (payload.FirstPath is { } path) SetLogo(path);
    }

    private void SetLogo(string path)
    {
        LogoPath = path;
        LogoSize = File.Exists(path) ? Formatters.Bytes(new FileInfo(path).Length) : string.Empty;
        var error = ModioModRules.ValidateImage(path);
        try
        {
            var bitmap = new Bitmap(path);
            if (error is null && (bitmap.PixelSize.Width < ModioModRules.LogoMinWidth || bitmap.PixelSize.Height < ModioModRules.LogoMinHeight))
                error = "ModioLogoTooSmall";
            Logo = bitmap;
        }
        catch
        {
            error ??= "ModioImageFormat";
        }
        LogoError = error switch
        {
            null => null,
            "ModioLogoTooSmall" => Loc[error],
            _ => $"{Path.GetFileName(path)}: {Loc[error]}",
        };
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        Logo = null;
    }
}
