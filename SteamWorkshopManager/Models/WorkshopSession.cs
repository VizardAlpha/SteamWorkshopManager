using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace SteamWorkshopManager.Models;

/// <summary>
/// Tracks a file/folder path with size and modification date for change detection.
/// </summary>
public class ItemFileInfo
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>
/// Represents a workshop session/profile for a specific game.
/// Each session contains all data related to managing mods for one Steam Workshop.
/// </summary>
public class WorkshopSession : INotifyPropertyChanged
{
    private Bitmap? _iconBitmap;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Cached Steam header image for the pill/flyout icons. Populated lazily,
    /// not serialized.
    /// </summary>
    [JsonIgnore]
    public Bitmap? IconBitmap
    {
        get => _iconBitmap;
        set
        {
            if (_iconBitmap == value) return;

            // Dispose the previous bitmap so repeated icon refreshes don't
            // accumulate native SkiaSharp surfaces between session switches.
            _iconBitmap?.Dispose();
            _iconBitmap = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Unique identifier for this session.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name (usually the game name).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Steam App ID for this game (e.g., 1162750 for Songs of Syx).
    /// </summary>
    public uint AppId { get; set; }

    /// <summary>
    /// Game name retrieved automatically from Steam Workshop page.
    /// </summary>
    public string? GameName { get; set; }

    /// <summary>
    /// Workshop tags organized by category.
    /// Key = category name, Value = list of tags in that category.
    /// </summary>
    public Dictionary<string, List<string>> TagsByCategory { get; set; } = new();

    /// <summary>
    /// Category names that should be displayed as dropdowns (single-select).
    /// </summary>
    public List<string> DropdownCategories { get; set; } = [];

    /// <summary>
    /// When the tags were last fetched from Steam.
    /// </summary>
    public DateTime TagsLastUpdated { get; set; }

    /// <summary>
    /// Custom tags created by the user for this workshop.
    /// </summary>
    public List<string> CustomTags { get; set; } = [];

    /// <summary>
    /// Remembered content folder info for each published item.
    /// Key = PublishedFileId as string.
    /// </summary>
    public Dictionary<string, ItemFileInfo> ContentFolderInfos { get; set; } = new();

    /// <summary>
    /// Remembered preview image info for each published item.
    /// Key = PublishedFileId as string.
    /// </summary>
    public Dictionary<string, ItemFileInfo> PreviewImageInfos { get; set; } = new();

    /// <summary>
    /// When this session was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this session was last used.
    /// </summary>
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    private bool _isActive;

    /// <summary>
    /// Whether this session is currently active (not serialized).
    /// </summary>
    [JsonIgnore]
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }
}
