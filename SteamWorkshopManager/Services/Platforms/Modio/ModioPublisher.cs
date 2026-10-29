using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;
using SteamWorkshopManager.Services.Workshop;

namespace SteamWorkshopManager.Services.Platforms.Modio;

/// <summary>Changes to send for an existing mod; null or empty leaves a part as it is.</summary>
public sealed record ModioModUpdate(
    ulong ModId,
    ModioModEdit Edit,
    IReadOnlyCollection<string> TagsToAdd,
    IReadOnlyCollection<string> TagsToRemove,
    string? LogoPath,
    ModioMediaChange Media,
    ModioFileUpload? File);

/// <summary>A new file: the folder is zipped before upload.</summary>
public sealed record ModioFileUpload(string Folder, string? Version, string? Changelog);

/// <summary>mod.io rules checked before anything is sent.</summary>
public static class ModioModRules
{
    public const int SummaryMaxLength = 250;
    public const int DescriptionMaxLength = 50_000;
    public const long ImageMaxBytes = 8L * 1024 * 1024;
    public const int LogoMinWidth = 512;
    public const int LogoMinHeight = 288;
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png"];

    /// <summary>Localization key of the first broken rule, null when the fields are fine.</summary>
    public static string? Validate(string? name, string? summary, string? description, string? homepageUrl)
    {
        if (string.IsNullOrWhiteSpace(name)) return "TitleRequired";
        if (string.IsNullOrWhiteSpace(summary)) return "ModioSummaryRequired";
        if (summary.Length > SummaryMaxLength) return "ModioSummaryTooLong";
        if (description?.Length > DescriptionMaxLength) return "ModioDescriptionTooLong";
        if (!string.IsNullOrWhiteSpace(homepageUrl) && !IsWebUrl(homepageUrl)) return "ModioHomepageInvalid";
        return null;
    }

    /// <summary>Format and size of a logo or gallery image; the logo's dimensions are checked by the caller.</summary>
    public static string? ValidateImage(string path)
    {
        if (!ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) return "ModioImageFormat";
        if (!File.Exists(path) || new FileInfo(path).Length > ImageMaxBytes) return "ModioImageTooLarge";
        return null;
    }

    public static bool IsWebUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}

/// <summary>Creates, edits and deletes mod.io mods of the active session.</summary>
/// <remarks>
/// Reads are cached per mod revision (mod.io's date_updated) and a day at most; every write
/// made here drops the mod's entries.
/// </remarks>
public sealed class ModioPublisher(ModioApiClient client, ModioAccount account, ISessionContext context, ICacheStore cache)
{
    private static readonly Logger Log = LogService.GetLogger<ModioPublisher>();
    private static readonly CachePolicy Policy = new(TimeSpan.FromDays(1), ServeStaleOnError: false);

    // Game settings change rarely; a stale copy is fine when mod.io can't be reached.
    private static readonly CachePolicy GamePolicy = new(TimeSpan.FromDays(7));

    private const string ModEntry = "mod.json";
    private const string FilesEntry = "files.json";
    private const string DependenciesEntry = "dependencies.json";

    /// <summary>Full mod, with what the list doesn't carry (gallery, homepage, community options).</summary>
    public async Task<ModioMod> GetModAsync(ulong modId, string? revision = null)
    {
        var gameId = ActiveGameIdUnsigned();
        return await cache.GetOrFetchAsync(Key(modId, ModEntry), ModioJsonContext.Default.ModioMod,
                   () => account.RunAsync<ModioMod?>(async auth => await client.GetModAsync(auth, gameId, (long)modId)),
                   Policy, forceRefresh: revision is null, version: revision)
               ?? throw new ModioApiException(0, 0, "mod.io returned no mod.");
    }

    /// <summary>First step of the create flow: the mod exists once this returns, without media or file yet.</summary>
    public async Task<ModioMod> CreateAsync(ModioNewMod mod)
    {
        var gameId = ActiveGameId();
        var created = await account.RunAsync(auth => client.AddModAsync(auth, gameId, mod));
        Log.Info($"mod.io mod {created.Id} created");
        return created;
    }

    /// <exception cref="ModioApiException">mod.io refused a step; the steps before it are kept.</exception>
    public async Task UpdateAsync(ModioModUpdate update, IProgress<UploadProgress>? progress = null)
    {
        var gameId = ActiveGameId();
        var modId = (long)update.ModId;
        try
        {
            progress?.Report(new UploadProgress("PreparingUpdate", 0, 0, 5));
            await account.RunAsync(auth => client.EditModAsync(auth, gameId, modId, update.Edit));

            if (update.TagsToRemove.Count > 0)
                await account.RunAsync(async auth => { await client.DeleteTagsAsync(auth, gameId, modId, update.TagsToRemove); return true; });
            if (update.TagsToAdd.Count > 0)
                await account.RunAsync(async auth => { await client.AddTagsAsync(auth, gameId, modId, update.TagsToAdd); return true; });

            if (update.LogoPath is { } logo)
            {
                progress?.Report(new UploadProgress("UploadingImage", 0, 0, 10));
                await account.RunAsync(async auth => { await client.SetLogoAsync(auth, gameId, modId, logo); return true; });
            }

            await SendMediaAsync(gameId, modId, update.Media, progress);

            if (update.File is { } file)
                await UploadContentAsync(gameId, modId, file, progress);

            Log.Info($"mod.io mod {modId} updated");
        }
        finally
        {
            Forget(update.ModId);
            // Closes the upload toast whatever happened.
            progress?.Report(new UploadProgress("Done", 0, 0, 100));
        }
    }

    public async Task UpdateMediaAsync(ulong modId, ModioMediaChange media, IProgress<UploadProgress>? progress = null)
    {
        try { await SendMediaAsync(ActiveGameId(), (long)modId, media, progress); }
        finally
        {
            Forget(modId);
            progress?.Report(new UploadProgress("Done", 0, 0, 100));
        }
    }

    public async Task UploadFileAsync(ulong modId, ModioFileUpload file, IProgress<UploadProgress>? progress = null)
    {
        try { await UploadContentAsync(ActiveGameId(), (long)modId, file, progress); }
        finally
        {
            Forget(modId);
            progress?.Report(new UploadProgress("Done", 0, 0, 100));
        }
    }

    // ─── Dependencies ────────────────────────────────────────────────────────

    /// <summary>The active game; its <c>dependency_option</c> says whether mods may have dependencies.</summary>
    public async Task<ModioGame> GetGameAsync()
    {
        var gameId = ActiveGameIdUnsigned();
        return await cache.GetOrFetchAsync(CacheKey.ModIoGame(gameId, "game.json"), ModioJsonContext.Default.ModioGame,
                   () => account.RunAsync<ModioGame?>(async auth => await client.GetGameAsync(auth, gameId)),
                   GamePolicy)
               ?? throw new ModioApiException(0, 0, "mod.io returned no game.");
    }

    public async Task<List<ModioModDependency>> GetDependenciesAsync(ulong modId, string? revision = null)
    {
        var gameId = ActiveGameIdUnsigned();
        return await cache.GetOrFetchAsync(Key(modId, DependenciesEntry), ModioJsonContext.Default.ListModioModDependency,
                   () => account.RunAsync<List<ModioModDependency>?>(async auth => await client.GetDependenciesAsync(auth, gameId, (long)modId)),
                   Policy, forceRefresh: revision is null, version: revision)
               ?? [];
    }

    public Task<IReadOnlyList<ModioMod>> SearchModsAsync(string query)
    {
        var gameId = ActiveGameId();
        return account.RunAsync(auth => client.SearchModsAsync(auth, gameId, query));
    }

    public async Task AddDependencyAsync(ulong modId, long dependencyId)
    {
        var gameId = ActiveGameId();
        try { await account.RunAsync(async auth => { await client.AddDependenciesAsync(auth, gameId, (long)modId, [dependencyId]); return true; }); }
        finally { cache.Remove(Key(modId, DependenciesEntry)); }
    }

    public async Task RemoveDependencyAsync(ulong modId, long dependencyId)
    {
        var gameId = ActiveGameId();
        try { await account.RunAsync(async auth => { await client.DeleteDependenciesAsync(auth, gameId, (long)modId, [dependencyId]); return true; }); }
        finally { cache.Remove(Key(modId, DependenciesEntry)); }
    }

    // ─── Published files ─────────────────────────────────────────────────────

    public async Task<List<ModioModfile>> GetFilesAsync(ulong modId, string? revision = null)
    {
        var gameId = ActiveGameIdUnsigned();
        return await cache.GetOrFetchAsync(Key(modId, FilesEntry), ModioJsonContext.Default.ListModioModfile,
                   () => account.RunAsync<List<ModioModfile>?>(async auth => await client.GetModfilesAsync(auth, gameId, (long)modId)),
                   Policy, forceRefresh: revision is null, version: revision)
               ?? [];
    }

    private CacheKey Key(ulong modId, string name) => CacheKey.ModIoMod(ActiveGameIdUnsigned(), modId, name);

    /// <summary>Our own write changed the mod: its cached reads are no longer right.</summary>
    private void Forget(ulong modId)
    {
        cache.Remove(Key(modId, ModEntry));
        cache.Remove(Key(modId, FilesEntry));
        cache.Remove(Key(modId, DependenciesEntry));
    }

    /// <summary>Makes an older file the one players download, without uploading anything.</summary>
    public async Task<ModioModfile> SetLiveFileAsync(ulong modId, long fileId)
    {
        var gameId = ActiveGameId();
        try { return await account.RunAsync(auth => client.EditModfileAsync(auth, gameId, (long)modId, fileId, active: true)); }
        finally { Forget(modId); }
    }

    public async Task<ModioModfile> EditFileAsync(ulong modId, long fileId, string version, string changelog)
    {
        var gameId = ActiveGameId();
        try { return await account.RunAsync(auth => client.EditModfileAsync(auth, gameId, (long)modId, fileId, version, changelog)); }
        finally { Forget(modId); }
    }

    public async Task DeleteFileAsync(ulong modId, long fileId)
    {
        var gameId = ActiveGameId();
        try { await account.RunAsync(async auth => { await client.DeleteModfileAsync(auth, gameId, (long)modId, fileId); return true; }); }
        finally { Forget(modId); }
    }

    /// <summary>Folder a published file is downloaded to, under the game's workshop folder.</summary>
    public string FileFolder(string modName, long fileId) =>
        AppPaths.ModioFileFolder(ActiveGameIdUnsigned(), WorkshopDownloadService.SanitizeModName(modName), fileId);

    public bool IsFileDownloaded(string modName, long fileId)
    {
        var folder = FileFolder(modName, fileId);
        return Directory.Exists(folder) && Directory.GetFiles(folder, "*.zip").Length > 0;
    }

    /// <summary>Downloads a published file; a fresh link is asked for since mod.io links can expire.</summary>
    public async Task<string> DownloadFileAsync(ulong modId, long fileId, string modName,
        IProgress<double>? progress = null, System.Threading.CancellationToken ct = default)
    {
        var gameId = ActiveGameId();
        var file = await account.RunAsync(auth => client.GetModfileAsync(auth, gameId, (long)modId, fileId, ct));
        var url = file.Download?.BinaryUrl ?? throw new ModioApiException(0, 0, "mod.io gave no download link for this file.");

        var folder = FileFolder(modName, fileId);
        Directory.CreateDirectory(folder);
        var name = string.IsNullOrEmpty(file.Filename) ? $"mod-{modId}-{fileId}.zip" : Path.GetFileName(file.Filename);
        var path = Path.Combine(folder, name);

        await client.DownloadAsync(url, path, file.Filehash?.Md5, progress, ct);
        Log.Info($"mod.io file {fileId} of mod {modId} downloaded");
        return path;
    }

    public async Task DeleteAsync(ulong modId)
    {
        var gameId = ActiveGameId();
        try { await account.RunAsync(async auth => { await client.DeleteModAsync(auth, gameId, (long)modId); return true; }); }
        finally { Forget(modId); }
    }

    private async Task SendMediaAsync(long gameId, long modId, ModioMediaChange media, IProgress<UploadProgress>? progress)
    {
        if (!media.HasRemovals && !media.HasAdditions) return;

        progress?.Report(new UploadProgress("UploadingImage", 0, 0, 15));
        if (media.HasRemovals)
            await account.RunAsync(async auth => { await client.DeleteMediaAsync(auth, gameId, modId, media); return true; });
        if (media.HasAdditions)
            await account.RunAsync(async auth => { await client.AddMediaAsync(auth, gameId, modId, media); return true; });
    }

    private async Task UploadContentAsync(long gameId, long modId, ModioFileUpload file, IProgress<UploadProgress>? progress)
    {
        progress?.Report(new UploadProgress("PreparingContent", 0, 0, 20));
        var dir = AppPaths.TempUploadDir();
        try
        {
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, $"mod-{modId}.zip");
            await Task.Run(() => ModioContentArchive.Create(file.Folder, zip));
            await account.RunAsync(auth => client.UploadModfileAsync(auth, gameId, modId, zip, file.Version, file.Changelog, progress));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { Log.Debug($"Upload temp folder not removed: {ex.Message}"); }
        }
    }

    private long ActiveGameId() => ActiveGameIdUnsigned();

    private uint ActiveGameIdUnsigned() =>
        context.Current is ModioSession session
            ? session.GameId
            : throw new InvalidOperationException("No mod.io session is active.");
}
