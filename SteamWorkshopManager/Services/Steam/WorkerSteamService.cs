using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Steamworks;
using StreamJsonRpc;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Session;
using SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

namespace SteamWorkshopManager.Services.Steam;

/// <summary>
/// Shell-side <see cref="ISteamService"/> that delegates every Steam call to
/// the out-of-process worker via <see cref="SessionHost"/>. The shell process
/// never touches <c>SteamAPI</c> directly once this implementation is wired
/// through DI — everything round-trips through JSON-RPC.
///
/// The sync <c>Initialize</c> / <c>Shutdown</c> surface on
/// <see cref="ISteamService"/> is preserved for compatibility with existing
/// callers; the real lifecycle is driven by <see cref="SessionHost"/>, which
/// is started up front by the app entry point.
///
/// Upload progress for Create/Update flows streams back over the same pipe:
/// StreamJsonRpc marshals <see cref="IProgress{T}"/> natively, so every
/// worker-side <c>Report</c> lands on the shell-side observer within the
/// same RPC session.
/// </summary>
public sealed class WorkerSteamService(SessionHost host) : ISteamService
{
    private static readonly Logger Log = LogService.GetLogger<WorkerSteamService>();

    public bool IsInitialized => host.LastInitResult == SteamInitResult.Success;

    public CSteamID? CurrentUserId => host.CurrentUserId == 0 ? null : new CSteamID(host.CurrentUserId);

    public SteamInitResult Initialize()
    {
        // The worker has already been spawned and initialized at session
        // start; we just report the cached outcome so existing callers work.
        return host.LastInitResult;
    }

    public void Shutdown()
    {
        // Worker shutdown is owned by SessionHost.Dispose. Swallow here so
        // legacy call sites don't double-tear-down.
    }

    public async Task<List<WorkshopItem>> GetPublishedItemsAsync()
    {
        if (host.Worker is null) return [];

        try
        {
            var dtos = await host.Worker.GetPublishedItemsAsync();
            return dtos.Select(FromDto).ToList();
        }
        catch (Exception ex)
        {
            LogRpcFailure(nameof(GetPublishedItemsAsync), ex);
            return [];
        }
    }

    public async Task<bool> DeleteItemAsync(PublishedFileId_t fileId)
    {
        if (host.Worker is null) return false;

        try
        {
            return await host.Worker.DeleteItemAsync(fileId.m_PublishedFileId);
        }
        catch (Exception ex)
        {
            LogRpcFailure(nameof(DeleteItemAsync), ex);
            return false;
        }
    }

    public List<GameBranch> GetGameBranches()
    {
        if (host.Worker is null) return [];

        try
        {
            var dtos = Task.Run(() => host.Worker.GetGameBranchesAsync()).GetAwaiter().GetResult();
            return dtos.Select(b => new GameBranch
            {
                Name = b.Name,
                Description = b.Description,
                BuildId = b.BuildId,
                Flags = b.Flags,
            }).ToList();
        }
        catch (Exception ex)
        {
            LogRpcFailure(nameof(GetGameBranches), ex);
            return [];
        }
    }

    public string GetCurrentBranchName()
    {
        if (host.Worker is null) return "public";

        try
        {
            return Task.Run(() => host.Worker.GetCurrentBranchNameAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogRpcFailure(nameof(GetCurrentBranchName), ex);
            return "public";
        }
    }

    /// <summary>
    /// A lost connection or a cancelled task almost always means a session
    /// switch killed the worker mid-call — the caller retries against the
    /// new worker anyway, so demote those to Debug to avoid noise. Everything
    /// else is a real failure worth the Error level.
    /// </summary>
    private static void LogRpcFailure(string operation, Exception ex)
    {
        if (ex is ConnectionLostException or OperationCanceledException)
            Log.Debug($"{operation} interrupted during session transition: {ex.Message}");
        else
            Log.Error($"{operation} RPC failed: {ex.Message}");
    }

    public async Task<PublishedFileId_t?> CreateItemAsync(
        string title, string description, string contentFolderPath,
        string? previewImagePath, VisibilityType visibility, List<string> tags, string? changelog,
        IProgress<UploadProgress>? progress = null, string? branchMin = null, string? branchMax = null,
        IReadOnlyList<PreviewOp>? previewOps = null)
    {
        if (host.Worker is null) return null;

        var request = new CreateItemRequestDto(
            title, description, contentFolderPath, previewImagePath,
            visibility, tags, changelog, branchMin, branchMax,
            previewOps?.Select(ToDto).ToList());

        try
        {
            var fileId = await host.Worker.CreateItemAsync(request, BridgeProgress(progress));
            return fileId == 0UL ? null : new PublishedFileId_t(fileId);
        }
        catch (Exception ex)
        {
            LogRpcFailure(nameof(CreateItemAsync), ex);
            return null;
        }
    }

    public async Task<bool> UpdateItemAsync(
        PublishedFileId_t fileId, string? title, string? description,
        string? contentFolderPath, string? previewImagePath, VisibilityType? visibility,
        List<string>? tags, string? changelog, IProgress<UploadProgress>? progress = null,
        string? branchMin = null, string? branchMax = null,
        IReadOnlyList<PreviewOp>? previewOps = null)
    {
        if (host.Worker is null) return false;

        var request = new UpdateItemRequestDto(
            fileId.m_PublishedFileId, title, description, contentFolderPath,
            previewImagePath, visibility, tags, changelog, branchMin, branchMax,
            previewOps?.Select(ToDto).ToList());

        try
        {
            return await host.Worker.UpdateItemAsync(request, BridgeProgress(progress));
        }
        catch (Exception ex)
        {
            LogRpcFailure(nameof(UpdateItemAsync), ex);
            return false;
        }
    }

    private static PreviewOpDto ToDto(PreviewOp op) => op switch
    {
        PreviewOp.Remove r => new PreviewOpDto(PreviewOpKind.Remove, r.Index, null, null),
        PreviewOp.AddImage img => new PreviewOpDto(PreviewOpKind.AddImage, null, img.FilePath, null),
        PreviewOp.AddVideo vid => new PreviewOpDto(PreviewOpKind.AddVideo, null, null, vid.YouTubeId),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>
    /// Wraps the caller's progress observer into the DTO channel that crosses
    /// the RPC boundary. Returning null when there's nothing to report avoids
    /// allocating a dead IProgress the worker would still dispatch to.
    /// </summary>
    private static IProgress<UploadProgressDto>? BridgeProgress(IProgress<UploadProgress>? sink)
    {
        if (sink is null) return null;
        return new Progress<UploadProgressDto>(p =>
            sink.Report(new UploadProgress(p.Status, p.BytesProcessed, p.BytesTotal, p.PercentHint)));
    }

    private static WorkshopItem FromDto(WorkshopItemDto dto) => new()
    {
        PublishedFileId = new PublishedFileId_t(dto.PublishedFileId),
        Title = dto.Title,
        Description = dto.Description,
        PreviewImagePath = dto.PreviewImagePath,
        PreviewImageUrl = dto.PreviewImageUrl,
        Visibility = dto.Visibility,
        Tags = dto.Tags.Select(t => new WorkshopTag(t.Name)).ToList(),
        CreatedAt = dto.CreatedAt,
        UpdatedAt = dto.UpdatedAt,
        SubscriberCount = dto.SubscriberCount,
        FileSize = dto.FileSize,
        OwnerId = new CSteamID(dto.OwnerId),
        IsOwner = dto.IsOwner,
        AdditionalPreviews = dto.AdditionalPreviews.Select(FromDto).ToList(),
    };

    private static WorkshopPreview FromDto(WorkshopPreviewDto dto) => new()
    {
        Source = WorkshopPreviewSource.Existing,
        PreviewType = (EItemPreviewType)dto.PreviewType,
        OriginalIndex = dto.OriginalIndex,
        RemoteUrl = dto.RemoteUrl,
        OriginalFilename = dto.OriginalFilename,
    };
}
