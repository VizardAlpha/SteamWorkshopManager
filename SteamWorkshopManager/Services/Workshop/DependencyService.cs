using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;
using Steamworks;

namespace SteamWorkshopManager.Services.Workshop;

/// <summary>
/// Shell-side facade over the worker's mod-dependency RPC. The raw SteamUGC
/// calls live in <c>SteamWorkerImpl</c> because Steam is only initialized in
/// the worker process; this class just marshals arguments and maps DTOs back
/// to domain models. The list is cached per item revision, at most a day, and
/// dropped after every add or remove made here.
/// </summary>
public sealed class DependencyService(SessionHost host, ICacheStore cache, ISessionContext context)
{
    private static readonly Logger Log = LogService.GetLogger<DependencyService>();
    internal static readonly CachePolicy Policy = new(TimeSpan.FromDays(1), ServeStaleOnError: false);

    private CacheKey Key(PublishedFileId_t parentId) =>
        CacheKey.SteamItem(context.AppId, parentId.m_PublishedFileId, "dependencies.json");

    public async Task<List<DependencyInfo>> GetDependenciesAsync(PublishedFileId_t parentId, string revision)
    {
        if (host.Worker is null) return [];

        var dtos = await cache.GetOrFetchAsync(Key(parentId), DependencyJsonContext.Default.ListDependencyInfoDto,
            async () => host.Worker is { } worker ? await worker.GetDependenciesAsync(parentId.m_PublishedFileId) : null,
            Policy, version: revision)
            ?? throw new InvalidOperationException("Steam did not return the dependencies.");
        return dtos.Select(ToDomain).ToList();
    }

    public async Task<bool> AddDependencyAsync(PublishedFileId_t parentId, PublishedFileId_t childId)
    {
        if (host.Worker is null) return false;
        Log.Info($"Adding dependency: parent={parentId}, child={childId}");
        var added = await host.Worker.AddDependencyAsync(parentId.m_PublishedFileId, childId.m_PublishedFileId);
        cache.Remove(Key(parentId));
        return added;
    }

    public async Task<bool> RemoveDependencyAsync(PublishedFileId_t parentId, PublishedFileId_t childId)
    {
        if (host.Worker is null) return false;
        Log.Info($"Removing dependency: parent={parentId}, child={childId}");
        var removed = await host.Worker.RemoveDependencyAsync(parentId.m_PublishedFileId, childId.m_PublishedFileId);
        cache.Remove(Key(parentId));
        return removed;
    }

    public async Task<DependencyInfo?> GetModDetailsAsync(PublishedFileId_t fileId)
    {
        if (host.Worker is null) return null;
        var dto = await host.Worker.GetModDetailsAsync(fileId.m_PublishedFileId);
        return dto is null ? null : ToDomain(dto);
    }

    private static DependencyInfo ToDomain(DependencyInfoDto dto) => new()
    {
        PublishedFileId = dto.PublishedFileId,
        Title = dto.Title,
        PreviewUrl = dto.PreviewUrl,
        IsValid = dto.IsValid,
    };
}

[JsonSerializable(typeof(List<DependencyInfoDto>))]
[JsonSerializable(typeof(List<AppDependencyInfoDto>))]
internal partial class DependencyJsonContext : JsonSerializerContext;
