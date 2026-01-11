using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Steamworks;

namespace SteamWorkshopManager.Models;

public class WorkshopItem : INotifyPropertyChanged
{
    private Bitmap? _previewBitmap;

    public required PublishedFileId_t PublishedFileId { get; init; }
    public required string Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? PreviewImagePath { get; set; }
    public string? PreviewImageUrl { get; set; }
    public string? ContentFolderPath { get; set; }
    public VisibilityType Visibility { get; set; } = VisibilityType.Private;
    public List<WorkshopTag> Tags { get; set; } = [];
    public List<ItemVersion> Versions { get; set; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }

    // Owner information
    public CSteamID OwnerId { get; init; }
    public bool IsOwner { get; init; }

    public Bitmap? PreviewBitmap
    {
        get => _previewBitmap;
        set
        {
            if (_previewBitmap != value)
            {
                _previewBitmap = value;
                OnPropertyChanged();
            }
        }
    }

    public string VisibilityIcon => Visibility switch
    {
        VisibilityType.Public => "\ud83c\udf10",
        VisibilityType.FriendsOnly => "\ud83d\udc65",
        VisibilityType.Private => "\ud83d\udd12",
        VisibilityType.Unlisted => "\ud83d\udd17",
        _ => "\u2753"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
