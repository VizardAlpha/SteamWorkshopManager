using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyType;
using StreamJsonRpc;
using SteamWorkshopManager.Services.Steam.Worker.Contracts.Dtos;

namespace SteamWorkshopManager.Services.Steam.Worker.Contracts;

/// <summary>
/// RPC contract exposed by the Steam worker process.
///
/// The shell uses <see cref="StreamJsonRpc.JsonRpc"/> to generate a proxy that
/// marshals calls to the worker over a named pipe. Keep this interface
/// serialization-friendly: every argument and return value must be a primitive,
/// enum, or DTO defined under <see cref="Dtos"/>.
///
/// Steamworks struct types (<c>PublishedFileId_t</c>, <c>CSteamID</c>) are
/// transported as raw <see cref="ulong"/>; the shell-side proxy rehydrates
/// them when mapping back to domain models.
/// </summary>
[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface ISteamWorker
{
    /// <summary>Liveness probe.</summary>
    Task<string> PingAsync();

    /// <summary>Attaches the shell-side log sink and aligns the worker's
    /// debug-mode flag with the user's setting. Pass <c>null</c> to detach.</summary>
    Task SetLogSinkAsync(IProgress<LogEntryDto>? sink, bool debugEnabled);

    /// <summary>Updates the worker's debug-mode flag at runtime when the
    /// user toggles it from settings.</summary>
    Task SetDebugModeAsync(bool enabled);

    /// <summary>Calls <c>SteamAPI.Init()</c> with the AppId the worker was spawned for.</summary>
    Task<SteamInitResult> InitializeAsync();

    /// <summary>Calls <c>SteamAPI.Shutdown()</c>.</summary>
    Task ShutdownAsync();

    /// <summary>Whether Steam is currently initialized in the worker process.</summary>
    Task<bool> IsInitializedAsync();

    /// <summary>Steam user id of the signed-in user, or 0 if not initialized.</summary>
    Task<ulong> GetCurrentUserIdAsync();

    /// <summary>All published Workshop items owned by the signed-in user.</summary>
    Task<List<WorkshopItemDto>> GetPublishedItemsAsync();

    /// <summary>Deletes a published Workshop item.</summary>
    Task<bool> DeleteItemAsync(ulong publishedFileId);

    /// <summary>
    /// Creates a new Workshop item and uploads its initial content. Returns the
    /// assigned PublishedFileId, or 0 on failure. Progress notifications are
    /// streamed over the RPC channel via <see cref="IProgress{T}"/>.
    /// </summary>
    Task<ulong> CreateItemAsync(CreateItemRequestDto request, IProgress<UploadProgressDto>? progress);

    /// <summary>
    /// Updates an existing Workshop item. Progress notifications are streamed
    /// over the RPC channel via <see cref="IProgress{T}"/>.
    /// </summary>
    Task<bool> UpdateItemAsync(UpdateItemRequestDto request, IProgress<UploadProgressDto>? progress);

    /// <summary>
    /// Available game branches for the AppId. Empty when versioning
    /// isn't supported by the game.
    /// </summary>
    Task<List<GameBranchDto>> GetGameBranchesAsync();

    /// <summary>Current active beta branch name (e.g. "public" on the default branch).</summary>
    Task<string> GetCurrentBranchNameAsync();

    /// <summary>Mod-to-mod dependencies declared on <paramref name="parentId"/>.</summary>
    Task<List<DependencyInfoDto>> GetDependenciesAsync(ulong parentId);

    /// <summary>Resolves a single Workshop item (for "Add dependency" preview card).</summary>
    Task<DependencyInfoDto?> GetModDetailsAsync(ulong fileId);

    /// <summary>Adds a child → parent dependency relation.</summary>
    Task<bool> AddDependencyAsync(ulong parentId, ulong childId);

    /// <summary>Removes a child → parent dependency relation.</summary>
    Task<bool> RemoveDependencyAsync(ulong parentId, ulong childId);

    /// <summary>App / DLC dependencies declared by <paramref name="modId"/>.</summary>
    Task<List<AppDependencyInfoDto>> GetAppDependenciesAsync(ulong modId);

    /// <summary>Declares <paramref name="appId"/> as a required app for <paramref name="modId"/>.</summary>
    Task<bool> AddAppDependencyAsync(ulong modId, uint appId);

    /// <summary>Removes an app dependency.</summary>
    Task<bool> RemoveAppDependencyAsync(ulong modId, uint appId);

    /// <summary>
    /// Supported-game-version entries (min/max branch pairs) declared on a
    /// Workshop item. Empty when the game isn't opted into the versioning
    /// feature or the item has no explicit version range.
    /// </summary>
    Task<List<ModVersionInfoDto>> GetSupportedGameVersionsAsync(ulong fileId);

    /// <summary>
    /// Issues a GET via the Steamworks HTTP API (anonymous, age-gate cookies
    /// applied). Lets the shell scrape Workshop pages without spinning up its
    /// own Steamworks instance — only the worker process has SteamAPI loaded.
    /// Returns the response body as UTF-8 string, or null on failure/timeout.
    /// </summary>
    Task<string?> FetchSteamWebAsync(string url);
}
