using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using Steamworks;

namespace SteamWorkshopManager.Services.Workshop;

/// <summary>
/// Shell-side facade over the worker's mod-dependency RPC. The raw SteamUGC
/// calls live in <c>SteamWorkerImpl</c> because Steam is only initialized in
/// the worker process; this class just marshals arguments and maps DTOs back
/// to domain models.
/// </summary>
public sealed class DependencyService(SessionHost host)
{
    private static readonly Logger Log = LogService.GetLogger<DependencyService>();

    public async Task<List<DependencyInfo>> GetDependenciesAsync(PublishedFileId_t parentId)
    {
        if (host.Worker is null) return [];
        var dtos = await host.Worker.GetDependenciesAsync(parentId.m_PublishedFileId);
        return dtos.Select(ToDomain).ToList();
    }

    public async Task<bool> AddDependencyAsync(PublishedFileId_t parentId, PublishedFileId_t childId)
    {
        if (host.Worker is null) return false;
        Log.Info($"Adding dependency: parent={parentId}, child={childId}");
        return await host.Worker.AddDependencyAsync(parentId.m_PublishedFileId, childId.m_PublishedFileId);
    }

    public async Task<bool> RemoveDependencyAsync(PublishedFileId_t parentId, PublishedFileId_t childId)
    {
        if (host.Worker is null) return false;
        Log.Info($"Removing dependency: parent={parentId}, child={childId}");
        return await host.Worker.RemoveDependencyAsync(parentId.m_PublishedFileId, childId.m_PublishedFileId);
    }

    public async Task<DependencyInfo?> GetModDetailsAsync(PublishedFileId_t fileId)
    {
        if (host.Worker is null) return null;
        var dto = await host.Worker.GetModDetailsAsync(fileId.m_PublishedFileId);
        return dto is null ? null : ToDomain(dto);
    }

    private static DependencyInfo ToDomain(Services.Steam.Worker.Contracts.Dtos.DependencyInfoDto dto) => new()
    {
        PublishedFileId = dto.PublishedFileId,
        Title = dto.Title,
        PreviewUrl = dto.PreviewUrl,
        IsValid = dto.IsValid,
    };
}
