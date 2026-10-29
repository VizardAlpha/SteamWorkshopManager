using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.ViewModels.Shell;

/// <summary>Sessions shown in the top-bar pill and its flyout, with their game icons.</summary>
public partial class SessionListViewModel(
    ISessionRepository repository,
    SteamAppMetadataService appMetadata,
    SteamImageCache images,
    ThumbnailCache thumbnails,
    ISessionContext context) : ViewModelBase
{
    private static readonly Logger Log = LogService.GetLogger<SessionListViewModel>();

    [ObservableProperty]
    private ObservableCollection<WorkshopSession> _sessions = [];

    [ObservableProperty]
    private Bitmap? _activeIcon;

    public WorkshopSession? Find(string id) => Sessions.FirstOrDefault(s => s.Id == id);

    public async Task LoadAsync()
    {
        try
        {
            var sessions = await repository.GetAllSessionsAsync();
            var activeId = context.Current?.Id;
            foreach (var session in sessions)
                session.IsActive = session.Id == activeId;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Fresh instances come from disk: carry decoded icons over instead of
                // re-decoding them, and release the ones of removed sessions.
                var previous = Sessions.ToDictionary(s => s.Id);
                foreach (var session in sessions)
                {
                    if (previous.Remove(session.Id, out var old) && old.IconBitmap is { } icon)
                    {
                        session.IconBitmap = icon;
                        old.DetachIconBitmap();
                    }
                }
                foreach (var removed in previous.Values)
                {
                    if (!ReferenceEquals(removed.IconBitmap, ActiveIcon)) removed.IconBitmap = null;
                }

                Sessions = new ObservableCollection<WorkshopSession>(sessions);
            });

            // Paint quickly with whatever is cached on disk (or the header fallback).
            foreach (var session in sessions.Where(s => s.IconBitmap is null))
                _ = LoadIconAsync(session);

            // Missing icon URLs go through PICS in a single batched round-trip.
            _ = EnsureIconUrlsAsync(sessions);
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to load sessions: {ex.Message}");
        }
    }

    /// <summary>Moves the active checkmark and the pill icon to <paramref name="id"/>.</summary>
    public void MarkActive(string id)
    {
        foreach (var s in Sessions)
            s.IsActive = s.Id == id;

        var active = Find(id);
        ActiveIcon = active?.IconBitmap;
        // The switch may have exposed a stale bitmap.
        if (active is not null) _ = LoadIconAsync(active);
    }

    public void Remove(string id)
    {
        if (Find(id) is { } stale) Sessions.Remove(stale);
    }

    private async Task LoadIconAsync(WorkshopSession session)
    {
        if (session is ModioSession { IconUrl: { } iconUrl })
        {
            if (await thumbnails.GetAsync(iconUrl, CancellationToken.None) is { } modioIcon)
                await ShowIconAsync(session, modioIcon);
            return;
        }
        if (session is not SteamSession steam) return;

        try
        {
            // Cache-only lookup: no URL needed when the icon is already on disk.
            var bitmap = await images.GetIconAsync(steam.AppId, iconUrl: null);

            // Fallback to the wide header while PICS is still resolving the icon hash.
            bitmap ??= await images.GetHeaderAsync(steam.AppId);
            if (bitmap is not null) await ShowIconAsync(session, bitmap);
        }
        catch (Exception ex)
        {
            Log.Debug($"LoadIconAsync failed for {session}: {ex.Message}");
        }
    }

    private async Task EnsureIconUrlsAsync(List<WorkshopSession> sessions)
    {
        // Only sessions whose icon isn't on disk yet need a PICS round-trip.
        var missing = sessions
            .OfType<SteamSession>()
            .Where(s => s.AppId > 0 && !images.HasIcon(s.AppId))
            .ToList();
        if (missing.Count == 0) return;

        try
        {
            var urls = await appMetadata.GetIconUrlsAsync(missing.Select(s => s.AppId));

            foreach (var session in missing)
            {
                if (!urls.TryGetValue(session.AppId, out var url)) continue;

                var bitmap = await images.GetIconAsync(session.AppId, url);
                if (bitmap is not null) await ShowIconAsync(session, bitmap);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"EnsureIconUrlsAsync failed: {ex.Message}");
        }
    }

    private async Task ShowIconAsync(WorkshopSession session, Bitmap bitmap) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            session.IconBitmap = bitmap;
            if (session.IsActive) ActiveIcon = bitmap;
        });
}
