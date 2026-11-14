using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkshopManager.Services.Steam;
using WorkshopManager.Services.Steam.Worker.Contracts;
using WorkshopManager.Services.Steam.Worker.Contracts.Dtos;

namespace WorkshopManager.Services.Session;

/// <summary>
/// The worker as the shell sees it. Every call first makes sure the process is running (it may be
/// on standby) and counts as activity, so <see cref="SessionHost"/> knows when Steam is idle.
/// </summary>
internal sealed class StandbyWorker(SessionHost host) : ISteamWorker
{
    private async Task<T> Run<T>(Func<ISteamWorker, Task<T>> call)
    {
        var worker = await host.AcquireAsync();
        try { return await call(worker); }
        finally { host.Release(); }
    }

    // Lifecycle and logging belong to the host: they never wake a worker on standby.
    public Task SetLogSinkAsync(IProgress<LogEntryDto>? sink, bool debugEnabled) =>
        host.RunningWorker?.SetLogSinkAsync(sink, debugEnabled) ?? Task.CompletedTask;

    public Task SetDebugModeAsync(bool enabled) =>
        host.RunningWorker?.SetDebugModeAsync(enabled) ?? Task.CompletedTask;

    public Task<SteamInitResult> InitializeAsync() => Task.FromResult(host.LastInitResult);

    public Task ShutdownAsync() => host.StopAsync();

    public Task<ulong> GetCurrentUserIdAsync() => Task.FromResult(host.CurrentUserId);

    public Task<List<WorkshopItemDto>> GetPublishedItemsAsync() => Run(w => w.GetPublishedItemsAsync());

    public Task<WorkshopItemDto?> GetPublishedItemAsync(ulong publishedFileId) =>
        Run(w => w.GetPublishedItemAsync(publishedFileId));

    public Task<bool> DeleteItemAsync(ulong publishedFileId) => Run(w => w.DeleteItemAsync(publishedFileId));

    public Task<CreateItemResultDto> CreateItemAsync(CreateItemRequestDto request, IProgress<UploadProgressDto>? progress) =>
        Run(w => w.CreateItemAsync(request, progress));

    public Task<UpdateItemResultDto> UpdateItemAsync(UpdateItemRequestDto request, IProgress<UploadProgressDto>? progress) =>
        Run(w => w.UpdateItemAsync(request, progress));

    public Task<ItemTextDto?> GetItemTextAsync(ulong publishedFileId, string language, bool fullDescription) =>
        Run(w => w.GetItemTextAsync(publishedFileId, language, fullDescription));

    public Task<bool> UpdateItemTextAsync(ulong publishedFileId, string language, string title, string description) =>
        Run(w => w.UpdateItemTextAsync(publishedFileId, language, title, description));

    public Task<List<GameBranchDto>> GetGameBranchesAsync() => Run(w => w.GetGameBranchesAsync());

    public Task<string> GetCurrentBranchNameAsync() => Run(w => w.GetCurrentBranchNameAsync());

    public Task<List<DependencyInfoDto>> GetDependenciesAsync(ulong parentId) => Run(w => w.GetDependenciesAsync(parentId));

    public Task<DependencyInfoDto?> GetModDetailsAsync(ulong fileId) => Run(w => w.GetModDetailsAsync(fileId));

    public Task<bool> AddDependencyAsync(ulong parentId, ulong childId) => Run(w => w.AddDependencyAsync(parentId, childId));

    public Task<bool> RemoveDependencyAsync(ulong parentId, ulong childId) =>
        Run(w => w.RemoveDependencyAsync(parentId, childId));

    public Task<List<AppDependencyInfoDto>> GetAppDependenciesAsync(ulong modId) => Run(w => w.GetAppDependenciesAsync(modId));

    public Task<bool> AddAppDependencyAsync(ulong modId, uint appId) => Run(w => w.AddAppDependencyAsync(modId, appId));

    public Task<bool> RemoveAppDependencyAsync(ulong modId, uint appId) => Run(w => w.RemoveAppDependencyAsync(modId, appId));

    public Task<List<ModVersionInfoDto>> GetSupportedGameVersionsAsync(ulong fileId) =>
        Run(w => w.GetSupportedGameVersionsAsync(fileId));

    public Task<string?> FetchSteamWebAsync(string url) => Run(w => w.FetchSteamWebAsync(url));
}
