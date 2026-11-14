using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using WorkshopManager.Helpers;
using WorkshopManager.Services.Log;
using WorkshopManager.Services.Steam.Worker.Contracts;
using WorkshopManager.Services.Steam.Worker.Contracts.Dtos;

namespace WorkshopManager.Services.Steam.Worker.Host;

/// <summary>
/// <see cref="ISteamWorker"/> running inside the worker process, the only one that calls
/// Steamworks.NET. A thin RPC mapper over <see cref="SteamService"/>, <see cref="SteamUgcQueries"/> and
/// <see cref="SteamUgcPublisher"/>.
/// </summary>
internal sealed class SteamWorkerImpl : ISteamWorker
{
    private static readonly Logger Log = LogService.GetLogger<SteamWorkerImpl>();

    private readonly SteamService _steam;
    private readonly SteamUgcQueries _queries;
    private readonly SteamUgcPublisher _publisher;
    private CancellationTokenSource? _logSinkCts;

    public SteamWorkerImpl(uint appId)
    {
        _steam = new SteamService(appId);
        _queries = new SteamUgcQueries(_steam, appId);
        _publisher = new SteamUgcPublisher(_steam, appId);
    }

    public async Task SetLogSinkAsync(IProgress<LogEntryDto>? sink, bool debugEnabled)
    {
        LogService.Instance.SetDebugMode(debugEnabled);

        _logSinkCts?.Cancel();
        _logSinkCts?.Dispose();
        _logSinkCts = null;

        if (sink == null)
        {
            LogService.Instance.SetRemoteSink(null);
            return;
        }

        LogService.Instance.SetRemoteSink(entry => sink.Report(new LogEntryDto(
            (int)entry.Level,
            entry.Source,
            entry.Message,
            entry.Exception,
            entry.Timestamp.ToUniversalTime())));

        // StreamJsonRpc IProgress<T> only stays alive for the duration of the
        // call that received it - return now and the sink dies. Hold the call
        // open until cancellation so every subsequent worker log keeps flowing.
        _logSinkCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(Timeout.Infinite, _logSinkCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            LogService.Instance.SetRemoteSink(null);
        }
    }

    public Task SetDebugModeAsync(bool enabled)
    {
        LogService.Instance.SetDebugMode(enabled);
        return Task.CompletedTask;
    }

    public Task<SteamInitResult> InitializeAsync() =>
        Task.FromResult(_steam.Initialize());

    public Task ShutdownAsync()
    {
        _steam.Shutdown();
        return Task.CompletedTask;
    }

    public Task<ulong> GetCurrentUserIdAsync() =>
        Task.FromResult(_steam.CurrentUserId?.m_SteamID ?? 0UL);

    public async Task<List<WorkshopItemDto>> GetPublishedItemsAsync()
    {
        var items = await _queries.GetPublishedItemsAsync();
        return items.Select(ToDto).ToList();
    }

    public async Task<WorkshopItemDto?> GetPublishedItemAsync(ulong publishedFileId)
    {
        var item = await _queries.GetPublishedItemAsync(new PublishedFileId_t(publishedFileId));
        return item is null ? null : ToDto(item);
    }

    public Task<bool> DeleteItemAsync(ulong publishedFileId) =>
        _publisher.DeleteItemAsync(new PublishedFileId_t(publishedFileId));

    public async Task<CreateItemResultDto> CreateItemAsync(CreateItemRequestDto request, IProgress<UploadProgressDto>? progress)
    {
        var bridge = BridgeProgress(progress);
        var outcome = await _publisher.CreateItemAsync(
            request.Title,
            request.Description,
            request.ContentFolderPath,
            request.PreviewImagePath,
            request.Visibility,
            request.Tags,
            request.Changelog,
            bridge,
            request.BranchMin,
            request.BranchMax,
            request.PreviewOps?.Select(FromDto).ToList());
        return new CreateItemResultDto(
            outcome.FileId?.m_PublishedFileId ?? 0UL,
            (int)(outcome.Result ?? 0),
            outcome.NeedsLegalAgreement);
    }

    public async Task<UpdateItemResultDto> UpdateItemAsync(UpdateItemRequestDto request, IProgress<UploadProgressDto>? progress)
    {
        var bridge = BridgeProgress(progress);
        var outcome = await _publisher.UpdateItemAsync(
            new PublishedFileId_t(request.PublishedFileId),
            request.Title,
            request.Description,
            request.ContentFolderPath,
            request.PreviewImagePath,
            request.Visibility,
            request.Tags,
            request.Changelog,
            bridge,
            request.BranchMin,
            request.BranchMax,
            request.PreviewOps?.Select(FromDto).ToList());
        return new UpdateItemResultDto(outcome.Success, outcome.NeedsLegalAgreement);
    }

    public async Task<ItemTextDto?> GetItemTextAsync(ulong publishedFileId, string language, bool fullDescription)
    {
        var text = await _queries.GetItemTextAsync(new PublishedFileId_t(publishedFileId), language, fullDescription);
        return text is null ? null : new ItemTextDto(text.Title, text.Description);
    }

    public Task<bool> UpdateItemTextAsync(ulong publishedFileId, string language, string title, string description) =>
        _publisher.UpdateItemTextAsync(new PublishedFileId_t(publishedFileId), language, new Models.ItemText(title, description));

    private static PreviewOp FromDto(PreviewOpDto dto) => dto.Kind switch
    {
        PreviewOpKind.Remove => new PreviewOp.Remove(dto.RemoveIndex ?? 0u),
        PreviewOpKind.AddImage => new PreviewOp.AddImage(dto.FilePath ?? ""),
        PreviewOpKind.AddVideo => new PreviewOp.AddVideo(dto.YouTubeId ?? ""),
        _ => throw new ArgumentOutOfRangeException(nameof(dto)),
    };

    /// <summary>
    /// Adapts the shell-bound <see cref="UploadProgressDto"/> channel back to
    /// the <see cref="UploadProgress"/> type the local publisher reports
    /// against. Returns null when no shell-side observer is attached, so we
    /// don't pay the allocation cost of a wrapper that reports into the void.
    /// </summary>
    private static IProgress<UploadProgress>? BridgeProgress(IProgress<UploadProgressDto>? sink)
    {
        if (sink is null) return null;
        // Direct: on the thread pool a late "Finalizing" could overtake the final report.
        return new DirectProgress<UploadProgress>(p =>
            sink.Report(new UploadProgressDto(p.Status, p.BytesProcessed, p.BytesTotal, p.PercentHint)));
    }

    public Task<List<DependencyInfoDto>> GetDependenciesAsync(ulong parentId) =>
        _queries.GetDependenciesAsync(parentId);

    public Task<DependencyInfoDto?> GetModDetailsAsync(ulong fileId) => _queries.GetModDetailsAsync(fileId);

    public Task<bool> AddDependencyAsync(ulong parentId, ulong childId) =>
        _publisher.AddDependencyAsync(parentId, childId);

    public Task<bool> RemoveDependencyAsync(ulong parentId, ulong childId) =>
        _publisher.RemoveDependencyAsync(parentId, childId);

    public Task<List<AppDependencyInfoDto>> GetAppDependenciesAsync(ulong modId) =>
        _queries.GetAppDependenciesAsync(modId);

    public Task<bool> AddAppDependencyAsync(ulong modId, uint appId) =>
        _publisher.AddAppDependencyAsync(modId, appId);

    public Task<bool> RemoveAppDependencyAsync(ulong modId, uint appId) =>
        _publisher.RemoveAppDependencyAsync(modId, appId);

    public Task<List<ModVersionInfoDto>> GetSupportedGameVersionsAsync(ulong fileId) =>
        _queries.GetSupportedGameVersionsAsync(fileId);

    public async Task<string?> FetchSteamWebAsync(string url)
    {
        // Routed here so the SteamHTTP cookie container & request lifecycle stay
        // inside the worker process - only here is SteamAPI initialised.
        SteamWebClient.Initialize();
        return await SteamWebClient.GetStringAsync(url);
    }

    public Task<List<GameBranchDto>> GetGameBranchesAsync()
    {
        var branches = _steam.GetGameBranches()
            .Select(b => new GameBranchDto(b.Name, b.Description, b.BuildId, b.Flags))
            .ToList();
        return Task.FromResult(branches);
    }

    public Task<string> GetCurrentBranchNameAsync() =>
        Task.FromResult(_steam.GetCurrentBranchName());

    private static WorkshopItemDto ToDto(Models.WorkshopItem item) => new(
        PublishedFileId: item.Id,
        Title: item.Title,
        Description: item.Description,
        PreviewImageUrl: item.PreviewImageUrl,
        Visibility: item.Visibility,
        Tags: item.Tags.Select(t => new WorkshopTagDto(t.Name)).ToList(),
        CreatedAt: item.CreatedAt,
        UpdatedAt: item.UpdatedAt,
        SubscriberCount: item.SubscriberCount,
        FileSize: item.FileSize,
        OwnerId: item.OwnerId,
        IsOwner: item.IsOwner,
        AdditionalPreviews: item.AdditionalPreviews.Select(p => new WorkshopPreviewDto(
            (int)p.PreviewType, p.OriginalIndex, p.RemoteUrl, p.OriginalFilename)).ToList()
    );
}
