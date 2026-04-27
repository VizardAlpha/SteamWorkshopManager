using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using SteamWorkshopManager.Helpers;
using Steamworks;

namespace SteamWorkshopManager.Models;

public class WorkshopItem : INotifyPropertyChanged
{
    private Bitmap? _previewBitmap;
    private bool _isSelected;

    public required PublishedFileId_t PublishedFileId { get; init; }
    public required string Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? PreviewImagePath { get; set; }
    public string? PreviewImageUrl { get; set; }
    public VisibilityType Visibility { get; set; } = VisibilityType.Private;
    public List<WorkshopTag> Tags { get; set; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Subscriber count reported by Steam (via GetQueryUGCStatistic).</summary>
    public ulong SubscriberCount { get; set; }

    /// <summary>Uploaded content file size in bytes (from SteamUGCDetails.m_nFileSize).</summary>
    public long FileSize { get; set; }

    // Owner information
    public CSteamID OwnerId { get; init; }
    public bool IsOwner { get; init; }

    /// <summary>
    /// Drives the bulk-action selection state in the item list. Lives on the
    /// model rather than a parallel collection so the checkbox overlay binds
    /// directly to the same instance the list iterates.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public Bitmap? PreviewBitmap
    {
        get => _previewBitmap;
        set
        {
            if (_previewBitmap == value) return;

            // Drop the previous bitmap's native buffer before swapping; otherwise
            // repeated session switches / item refreshes accumulate SkiaSharp
            // surfaces until the GC eventually collects them.
            _previewBitmap?.Dispose();
            _previewBitmap = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Compact subscriber count for list/grid display (e.g. "1.2K").</summary>
    public string SubscribersDisplay => Formatters.CompactNumber((long)SubscriberCount);

    /// <summary>Relative "time ago" for list/grid display (e.g. "2d ago").</summary>
    public string UpdatedAtDisplay => Formatters.TimeAgo(UpdatedAt);

    public bool IsVisibilityPublic => Visibility == VisibilityType.Public;
    public bool IsVisibilityFriends => Visibility == VisibilityType.FriendsOnly;
    public bool IsVisibilityPrivate => Visibility == VisibilityType.Private;
    public bool IsVisibilityUnlisted => Visibility == VisibilityType.Unlisted;

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
