using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Notifications;
using SteamWorkshopManager.Services.Platforms.Modio;

namespace SteamWorkshopManager.ViewModels.Modio;

/// <summary>A mod in the dependency list or in the search results.</summary>
public partial class ModioDependencyEntry : ObservableObject, IDisposable
{
    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnThumbnailChanging(Bitmap? value) => _thumbnail?.Dispose();

    public required long ModId { get; init; }
    public required string Name { get; init; }
    public string? ProfileUrl { get; init; }
    public string? LogoUrl { get; init; }

    public void Dispose() => Thumbnail = null;
}

/// <summary>
/// Dependencies section of a mod.io mod, shown when the game allows them. Changes apply
/// at once, like on mod.io's page; whether other mods may depend on this one is a community
/// option saved with the rest of the editor.
/// </summary>
public partial class ModioDependenciesViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogService.GetLogger<ModioDependenciesViewModel>();

    // mod.io's game dependency_option values.
    private const int DisallowDependencies = 0;
    private const int OptIn = 1;
    private const int OptOut = 2;

    private readonly ModioPublisher _publisher;
    private readonly ThumbnailCache _thumbnails;
    private readonly INotificationService _notifications;
    private readonly CancellationTokenSource _cts = new();

    private ulong _modId;
    private string? _revision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllowedByGame), nameof(ShowDependentsToggle))]
    private int _gameOption = DisallowDependencies;

    /// <summary>Other mods may list this one (community option 1024).</summary>
    [ObservableProperty]
    private bool _allowDependents;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasNoResults;

    public ObservableCollection<ModioDependencyEntry> Dependencies { get; } = [];

    public ObservableCollection<ModioDependencyEntry> SearchResults { get; } = [];

    public bool HasDependencies => Dependencies.Count > 0;

    public bool IsAllowedByGame => GameOption != DisallowDependencies;

    /// <summary>Only opt-in and opt-out games let each mod choose; the other two settings decide for it.</summary>
    public bool ShowDependentsToggle => GameOption is OptIn or OptOut;

    public ModioDependenciesViewModel(ModioPublisher publisher, ThumbnailCache thumbnails, INotificationService notifications)
    {
        _publisher = publisher;
        _thumbnails = thumbnails;
        _notifications = notifications;
        Dependencies.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDependencies));
    }

    /// <summary>Reads the game's setting, then the mod's dependencies when the game allows them.</summary>
    public async Task LoadAsync(ulong modId, string revision, int communityOptions)
    {
        _modId = modId;
        _revision = revision;
        AllowDependents = (communityOptions & ModioCommunity.Dependencies) != 0;

        IsLoading = true;
        Error = null;
        try
        {
            GameOption = (await _publisher.GetGameAsync()).DependencyOption;
            if (!IsAllowedByGame) return;

            var dependencies = await _publisher.GetDependenciesAsync(modId, _revision);
            Replace(Dependencies, dependencies.Select(d => new ModioDependencyEntry
            {
                ModId = d.ModId,
                Name = d.Name ?? $"#{d.ModId}",
                ProfileUrl = d.ProfileUrl,
                LogoUrl = d.Logo?.Thumb320X180,
            }));
        }
        catch (Exception ex)
        {
            Log.Warning($"mod.io dependencies failed: {ex.Message}");
            Error = ex is ModioApiException api ? ModioErrors.Describe(api, "ModioDependenciesFailed") : Loc["ModioDependenciesFailed"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Community bits with this section's choice applied, when the game lets the mod choose.</summary>
    public int ApplyTo(int communityOptions) => ShowDependentsToggle
        ? ModioCommunity.With(communityOptions, ModioCommunity.Dependencies, AllowDependents)
        : communityOptions;

    private bool CanSearch => !IsSearching;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        if (query.Length == 0) return;

        IsSearching = true;
        Error = null;
        HasNoResults = false;
        try
        {
            var mods = await _publisher.SearchModsAsync(query);
            Replace(SearchResults, mods
                .Where(m => (ulong)m.Id != _modId && Dependencies.All(d => d.ModId != m.Id))
                .Select(m => new ModioDependencyEntry
                {
                    ModId = m.Id,
                    Name = m.Name,
                    ProfileUrl = m.ProfileUrl,
                    LogoUrl = m.Logo?.Thumb320X180,
                }));
            HasNoResults = SearchResults.Count == 0;
        }
        catch (Exception ex)
        {
            Error = ex is ModioApiException api ? ModioErrors.Describe(api, "ModioSearchFailed") : ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private Task AddAsync(ModioDependencyEntry entry) => RunAsync(entry, async () =>
    {
        await _publisher.AddDependencyAsync(_modId, entry.ModId);
        SearchResults.Remove(entry);
        Dependencies.Add(entry);
        _notifications.ShowSuccess(string.Format(Loc["ModioDependencyAdded"], entry.Name));
    });

    [RelayCommand]
    private Task RemoveAsync(ModioDependencyEntry entry) => RunAsync(entry, async () =>
    {
        await _publisher.RemoveDependencyAsync(_modId, entry.ModId);
        Dependencies.Remove(entry);
        entry.Dispose();
    });

    [RelayCommand]
    private static void OpenModPage(ModioDependencyEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ProfileUrl)) return;
        try { Process.Start(new ProcessStartInfo(entry.ProfileUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Debug($"Failed to open the mod page: {ex.Message}"); }
    }

    private async Task RunAsync(ModioDependencyEntry entry, Func<Task> action)
    {
        if (entry.IsBusy) return;
        entry.IsBusy = true;
        Error = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Error = ex is ModioApiException api ? ModioErrors.Describe(api, "ModioDependencyFailed") : ex.Message;
        }
        finally
        {
            entry.IsBusy = false;
        }
    }

    private void Replace(ObservableCollection<ModioDependencyEntry> target, System.Collections.Generic.IEnumerable<ModioDependencyEntry> entries)
    {
        foreach (var old in target) old.Dispose();
        target.Clear();
        foreach (var entry in entries)
        {
            target.Add(entry);
            if (entry.LogoUrl is { } url) _ = LoadThumbnailAsync(entry, url);
        }
    }

    private async Task LoadThumbnailAsync(ModioDependencyEntry entry, string url)
    {
        var bitmap = await _thumbnails.GetAsync(url, _cts.Token);
        if (bitmap is null || _cts.IsCancellationRequested) bitmap?.Dispose();
        else entry.Thumbnail = bitmap;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        foreach (var entry in Dependencies.Concat(SearchResults)) entry.Dispose();
    }
}
