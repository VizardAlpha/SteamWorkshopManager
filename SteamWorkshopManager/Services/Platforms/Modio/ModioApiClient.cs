using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using SteamWorkshopManager.Services.Log;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Services.Platforms.Modio;

/// <summary>Fields of a new mod. mod.io requires a logo.</summary>
public sealed record ModioNewMod(
    string Name,
    string Summary,
    string LogoPath,
    string? Description,
    bool Visible,
    IReadOnlyList<string> Tags,
    string? HomepageUrl = null,
    int CommunityOptions = ModioCommunity.Default);

/// <summary>Fields to change on a mod; null leaves a field as it is.</summary>
public sealed record ModioModEdit(
    string? Name = null,
    string? Summary = null,
    string? Description = null,
    bool? Visible = null,
    string? HomepageUrl = null,
    int? CommunityOptions = null);

/// <summary>Community option bits of a mod. Only comments and collections are offered here; other bits are kept.</summary>
public static class ModioCommunity
{
    public const int Comments = 1;
    public const int Dependencies = 1024;
    public const int Collections = 131072;
    public const int Default = Comments | Collections;

    public static int With(int options, int bit, bool enabled) => enabled ? options | bit : options & ~bit;
}

/// <summary>Gallery changes; images are added from local files, removed by their mod.io file name.</summary>
public sealed record ModioMediaChange(
    IReadOnlyList<string> AddImages,
    IReadOnlyList<string> AddYoutube,
    IReadOnlyList<string> AddSketchfab,
    IReadOnlyList<string> RemoveImages,
    IReadOnlyList<string> RemoveYoutube,
    IReadOnlyList<string> RemoveSketchfab)
{
    public static ModioMediaChange None { get; } = new([], [], [], [], [], []);

    public bool HasAdditions => AddImages.Count + AddYoutube.Count + AddSketchfab.Count > 0;
    public bool HasRemovals => RemoveImages.Count + RemoveYoutube.Count + RemoveSketchfab.Count > 0;
}

/// <summary>
/// HTTP client over the mod.io REST API, authenticated with the user's own token
/// (<see cref="ModioCredentials"/>, passed per call). Errors become
/// <see cref="ModioApiException"/>. After a 429 every call fails fast until the
/// retry delay elapses: mod.io may revoke credentials that keep sending.
/// </summary>
public sealed class ModioApiClient
{
    private static readonly Logger Log = LogService.GetLogger<ModioApiClient>();

    /// <summary>mod.io takes up to 500 MB in one request but recommends multipart above 100 MB.</summary>
    internal const long MultipartThreshold = 100L * 1024 * 1024;

    /// <summary>Every part but the last must be exactly 50 MiB.</summary>
    internal const int PartSize = 50 * 1024 * 1024;

    private const int PageSize = 100;

    // A retry-after of 0 means a rolling limit: mod.io advises waiting a minute.
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly long _multipartThreshold;
    private readonly int _partSize;
    private readonly Lock _rateLock = new();
    private DateTimeOffset _blockedUntil;

    public ModioApiClient() : this(new HttpClientHandler(), TimeProvider.System) { }

    internal ModioApiClient(HttpMessageHandler handler, TimeProvider time,
        long multipartThreshold = MultipartThreshold, int partSize = PartSize)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(SteamHttpClientFactory.UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _time = time;
        _multipartThreshold = multipartThreshold;
        _partSize = partSize;
    }

    // ─── Account and games ───────────────────────────────────────────────────

    /// <summary>The token's owner. Also how a pasted token is checked.</summary>
    public Task<ModioUser> GetMeAsync(ModioCredentials auth, CancellationToken ct = default) =>
        GetAsync(auth, "me", ModioJsonContext.Default.ModioUser, ct);

    public async Task<IReadOnlyList<ModioGame>> SearchGamesAsync(ModioCredentials auth, string query, CancellationToken ct = default)
    {
        var page = await GetAsync(auth, $"games?_q={Uri.EscapeDataString(query)}&_limit=50",
            ModioJsonContext.Default.PagedGames, ct);
        return page.Data;
    }

    public Task<ModioGame> GetGameAsync(ModioCredentials auth, long gameId, CancellationToken ct = default) =>
        GetAsync(auth, $"games/{gameId}", ModioJsonContext.Default.ModioGame, ct);

    public Task<List<ModioTagOption>> GetTagOptionsAsync(ModioCredentials auth, long gameId, CancellationToken ct = default) =>
        GetAllAsync(auth, $"games/{gameId}/tags", ModioJsonContext.Default.PagedTagOptions, ct);

    // ─── Mods ────────────────────────────────────────────────────────────────

    /// <summary>Mods the user added or is a team member of, for one game, latest update first.</summary>
    public Task<List<ModioMod>> GetMyModsAsync(ModioCredentials auth, long gameId, CancellationToken ct = default) =>
        GetAllAsync(auth, $"me/mods?game_id={gameId}&_sort=-date_updated", ModioJsonContext.Default.PagedMods, ct);

    public Task<ModioMod> GetModAsync(ModioCredentials auth, long gameId, long modId, CancellationToken ct = default) =>
        GetAsync(auth, $"games/{gameId}/mods/{modId}", ModioJsonContext.Default.ModioMod, ct);

    public async Task<ModioMod> AddModAsync(ModioCredentials auth, long gameId, ModioNewMod mod, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(mod.Name), "name");
        form.Add(new StringContent(mod.Summary), "summary");
        form.Add(new StringContent(mod.Visible ? "1" : "0"), "visible");
        if (!string.IsNullOrEmpty(mod.Description)) form.Add(new StringContent(mod.Description), "description");
        if (!string.IsNullOrEmpty(mod.HomepageUrl)) form.Add(new StringContent(mod.HomepageUrl), "homepage_url");
        form.Add(new StringContent(mod.CommunityOptions.ToString(CultureInfo.InvariantCulture)), "community_options");
        foreach (var tag in mod.Tags) form.Add(new StringContent(tag), "tags[]");
        AddFile(form, "logo", mod.LogoPath);

        using var req = new HttpRequestMessage(HttpMethod.Post, Url(auth, $"games/{gameId}/mods")) { Content = form };
        using var resp = await SendAsync(auth, req, ct);
        return await ReadAsync(resp, ModioJsonContext.Default.ModioMod, ct);
    }

    /// <summary>Edits text and visibility. Logo, tags and files have their own endpoints.</summary>
    public async Task<ModioMod?> EditModAsync(ModioCredentials auth, long gameId, long modId, ModioModEdit edit, CancellationToken ct = default)
    {
        var fields = new List<(string Name, string Value)>();
        if (edit.Name is not null) fields.Add(("name", edit.Name));
        if (edit.Summary is not null) fields.Add(("summary", edit.Summary));
        if (edit.Description is not null) fields.Add(("description", edit.Description));
        if (edit.Visible is { } visible) fields.Add(("visible", visible ? "1" : "0"));
        if (edit.HomepageUrl is not null) fields.Add(("homepage_url", edit.HomepageUrl));
        if (edit.CommunityOptions is { } community) fields.Add(("community_options", community.ToString(CultureInfo.InvariantCulture)));
        if (fields.Count == 0) return null;

        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields) form.Add(new StringContent(value), name);

        using var req = new HttpRequestMessage(HttpMethod.Post, Url(auth, $"games/{gameId}/mods/{modId}")) { Content = form };
        using var resp = await SendAsync(auth, req, ct);
        return await ReadAsync(resp, ModioJsonContext.Default.ModioMod, ct);
    }

    public async Task DeleteModAsync(ModioCredentials auth, long gameId, long modId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, Url(auth, $"games/{gameId}/mods/{modId}"));
        using var _ = await SendAsync(auth, req, ct);
    }

    public async Task SetLogoAsync(ModioCredentials auth, long gameId, long modId, string logoPath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        AddFile(form, "logo", logoPath);
        using var req = new HttpRequestMessage(HttpMethod.Post, Url(auth, $"games/{gameId}/mods/{modId}/media")) { Content = form };
        using var _ = await SendAsync(auth, req, ct);
    }

    /// <summary>Appends images, YouTube videos and Sketchfab models to the gallery.</summary>
    public async Task AddMediaAsync(ModioCredentials auth, long gameId, long modId, ModioMediaChange change, CancellationToken ct = default)
    {
        if (!change.HasAdditions) return;

        using var form = new MultipartFormDataContent();
        // mod.io takes every posted image whatever its field name.
        for (var i = 0; i < change.AddImages.Count; i++) AddFile(form, $"image{i}", change.AddImages[i]);
        foreach (var url in change.AddYoutube) form.Add(new StringContent(url), "youtube[]");
        foreach (var url in change.AddSketchfab) form.Add(new StringContent(url), "sketchfab[]");

        using var req = new HttpRequestMessage(HttpMethod.Post, Url(auth, $"games/{gameId}/mods/{modId}/media")) { Content = form };
        using var _ = await SendAsync(auth, req, ct);
    }

    public async Task DeleteMediaAsync(ModioCredentials auth, long gameId, long modId, ModioMediaChange change, CancellationToken ct = default)
    {
        if (!change.HasRemovals) return;

        var fields = new List<KeyValuePair<string, string>>();
        foreach (var name in change.RemoveImages) fields.Add(new("images[]", name));
        foreach (var url in change.RemoveYoutube) fields.Add(new("youtube[]", url));
        foreach (var url in change.RemoveSketchfab) fields.Add(new("sketchfab[]", url));

        using var req = new HttpRequestMessage(HttpMethod.Delete, Url(auth, $"games/{gameId}/mods/{modId}/media"))
        {
            Content = new FormUrlEncodedContent(fields),
        };
        using var _ = await SendAsync(auth, req, ct);
    }

    /// <summary>Public mods of a game matching <paramref name="query"/>, to pick a dependency.</summary>
    public async Task<IReadOnlyList<ModioMod>> SearchModsAsync(ModioCredentials auth, long gameId, string query, CancellationToken ct = default)
    {
        var page = await GetAsync(auth, $"games/{gameId}/mods?_q={Uri.EscapeDataString(query)}&_limit=20",
            ModioJsonContext.Default.PagedMods, ct);
        return page.Data;
    }

    /// <summary>Direct dependencies of a mod (not their own dependencies).</summary>
    public Task<List<ModioModDependency>> GetDependenciesAsync(ModioCredentials auth, long gameId, long modId, CancellationToken ct = default) =>
        GetAllAsync(auth, $"games/{gameId}/mods/{modId}/dependencies?recursive=false", ModioJsonContext.Default.PagedDependencies, ct);

    public Task AddDependenciesAsync(ModioCredentials auth, long gameId, long modId, IEnumerable<long> dependencyIds, CancellationToken ct = default) =>
        SendDependenciesAsync(auth, HttpMethod.Post, gameId, modId, dependencyIds, ct);

    public Task DeleteDependenciesAsync(ModioCredentials auth, long gameId, long modId, IEnumerable<long> dependencyIds, CancellationToken ct = default) =>
        SendDependenciesAsync(auth, HttpMethod.Delete, gameId, modId, dependencyIds, ct);

    private async Task SendDependenciesAsync(ModioCredentials auth, HttpMethod method, long gameId, long modId,
        IEnumerable<long> dependencyIds, CancellationToken ct)
    {
        var fields = dependencyIds
            .Select(id => new KeyValuePair<string, string>("dependencies[]", id.ToString(CultureInfo.InvariantCulture)))
            .ToList();
        if (fields.Count == 0) return;

        using var req = new HttpRequestMessage(method, Url(auth, $"games/{gameId}/mods/{modId}/dependencies"))
        {
            Content = new FormUrlEncodedContent(fields),
        };
        using var _ = await SendAsync(auth, req, ct);
    }

    public Task AddTagsAsync(ModioCredentials auth, long gameId, long modId, IEnumerable<string> tags, CancellationToken ct = default) =>
        SendTagsAsync(auth, HttpMethod.Post, gameId, modId, tags, ct);

    public Task DeleteTagsAsync(ModioCredentials auth, long gameId, long modId, IEnumerable<string> tags, CancellationToken ct = default) =>
        SendTagsAsync(auth, HttpMethod.Delete, gameId, modId, tags, ct);

    private async Task SendTagsAsync(ModioCredentials auth, HttpMethod method, long gameId, long modId, IEnumerable<string> tags, CancellationToken ct)
    {
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var tag in tags) fields.Add(new("tags[]", tag));
        if (fields.Count == 0) return;

        using var req = new HttpRequestMessage(method, Url(auth, $"games/{gameId}/mods/{modId}/tags"))
        {
            Content = new FormUrlEncodedContent(fields),
        };
        using var _ = await SendAsync(auth, req, ct);
    }

    // ─── Files ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Uploads a zip as the mod's new live file, in one request up to 100 MB and in 50 MiB
    /// parts above. mod.io checks the MD5 we send against what it received.
    /// </summary>
    public async Task<ModioModfile> UploadModfileAsync(
        ModioCredentials auth, long gameId, long modId, string zipPath,
        string? version, string? changelog,
        IProgress<UploadProgress>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(new UploadProgress("PreparingContent", 0, 0));
        var length = new FileInfo(zipPath).Length;
        var md5 = await ComputeMd5Async(zipPath, ct);

        var uploadId = length > _multipartThreshold
            ? await UploadPartsAsync(auth, gameId, modId, zipPath, length, progress, ct)
            : null;

        using var form = new MultipartFormDataContent();
        Stream? file = null;
        try
        {
            if (uploadId is null)
            {
                file = File.OpenRead(zipPath);
                var bytes = new Progress<long>(sent =>
                    progress?.Report(new UploadProgress("UploadingContent", (ulong)sent, (ulong)length)));
                var content = new ProgressStreamContent(file, length, bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                form.Add(content, "filedata", ArchiveName(zipPath));
            }
            else
            {
                form.Add(new StringContent(uploadId), "upload_id");
            }
            form.Add(new StringContent(md5), "filehash");
            if (!string.IsNullOrEmpty(version)) form.Add(new StringContent(version), "version");
            if (!string.IsNullOrEmpty(changelog)) form.Add(new StringContent(changelog), "changelog");

            using var req = new HttpRequestMessage(HttpMethod.Post, Url(auth, $"games/{gameId}/mods/{modId}/files")) { Content = form };
            using var resp = await SendAsync(auth, req, ct);
            progress?.Report(new UploadProgress("Finalizing", 0, 0, 100));
            return await ReadAsync(resp, ModioJsonContext.Default.ModioModfile, ct);
        }
        finally
        {
            if (file is not null) await file.DisposeAsync();
        }
    }

    /// <summary>Sends the file through a multipart session and returns its id once complete.</summary>
    private async Task<string> UploadPartsAsync(
        ModioCredentials auth, long gameId, long modId, string zipPath, long length,
        IProgress<UploadProgress>? progress, CancellationToken ct)
    {
        var filesPath = $"games/{gameId}/mods/{modId}/files/multipart";

        string uploadId;
        using (var create = new HttpRequestMessage(HttpMethod.Post, Url(auth, filesPath)))
        {
            create.Content = new FormUrlEncodedContent([
                new("filename", ArchiveName(zipPath)),
                new("nonce", Guid.NewGuid().ToString("N")),
            ]);
            using var resp = await SendAsync(auth, create, ct);
            uploadId = (await ReadAsync(resp, ModioJsonContext.Default.ModioMultipartUpload, ct)).UploadId
                       ?? throw new ModioApiException(0, 0, "mod.io did not return an upload id.");
        }

        var query = $"?upload_id={Uri.EscapeDataString(uploadId)}";
        var buffer = new byte[(int)Math.Min(_partSize, length)];
        await using (var file = File.OpenRead(zipPath))
        {
            long start = 0;
            while (start < length)
            {
                var read = await file.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
                if (read == 0) break;

                using var part = new HttpRequestMessage(HttpMethod.Put, Url(auth, filesPath + query))
                {
                    Content = new ByteArrayContent(buffer, 0, read),
                };
                part.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, start + read - 1, length);
                using (await SendAsync(auth, part, ct)) { }

                start += read;
                progress?.Report(new UploadProgress("UploadingContent", (ulong)start, (ulong)length));
            }
        }

        using var complete = new HttpRequestMessage(HttpMethod.Post, Url(auth, filesPath + "/complete" + query));
        using (await SendAsync(auth, complete, ct)) { }
        return uploadId;
    }

    /// <summary>Every published file of a mod, newest first.</summary>
    public Task<List<ModioModfile>> GetModfilesAsync(ModioCredentials auth, long gameId, long modId, CancellationToken ct = default) =>
        GetAllAsync(auth, $"games/{gameId}/mods/{modId}/files?_sort=-date_added", ModioJsonContext.Default.PagedModfiles, ct);

    public Task<ModioModfile> GetModfileAsync(ModioCredentials auth, long gameId, long modId, long fileId, CancellationToken ct = default) =>
        GetAsync(auth, $"games/{gameId}/mods/{modId}/files/{fileId}", ModioJsonContext.Default.ModioModfile, ct);

    /// <summary>Changes a file's version or changelog, or makes it the live one (<paramref name="active"/>).</summary>
    public async Task<ModioModfile> EditModfileAsync(ModioCredentials auth, long gameId, long modId, long fileId,
        string? version = null, string? changelog = null, bool? active = null, CancellationToken ct = default)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (version is not null) fields.Add(new("version", version));
        if (changelog is not null) fields.Add(new("changelog", changelog));
        if (active is { } isActive) fields.Add(new("active", isActive ? "true" : "false"));

        using var req = new HttpRequestMessage(HttpMethod.Put, Url(auth, $"games/{gameId}/mods/{modId}/files/{fileId}"))
        {
            Content = new FormUrlEncodedContent(fields),
        };
        using var resp = await SendAsync(auth, req, ct);
        return await ReadAsync(resp, ModioJsonContext.Default.ModioModfile, ct);
    }

    public async Task DeleteModfileAsync(ModioCredentials auth, long gameId, long modId, long fileId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, Url(auth, $"games/{gameId}/mods/{modId}/files/{fileId}"));
        using var _ = await SendAsync(auth, req, ct);
    }

    /// <summary>
    /// Downloads a file from its <c>binary_url</c> to <paramref name="destination"/>, written as
    /// <c>.part</c> then renamed, and checked against <paramref name="md5"/> when given. The link
    /// is self-contained: the token is not sent to the CDN.
    /// </summary>
    public async Task DownloadAsync(string binaryUrl, string destination, string? md5,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ThrowIfRateLimited();
        var part = destination + ".part";
        try
        {
            using (var resp = await _http.GetAsync(binaryUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!resp.IsSuccessStatusCode) throw await ToExceptionAsync(resp, resp.RequestMessage!, ct);

                var total = resp.Content.Headers.ContentLength ?? -1;
                await using var source = await resp.Content.ReadAsStreamAsync(ct);
                await using var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            if (md5 is not null && !string.Equals(await ComputeMd5Async(part, ct), md5, StringComparison.OrdinalIgnoreCase))
                throw new ModioApiException(0, 0, "The downloaded file does not match mod.io's checksum.");

            File.Move(part, destination, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); }
            catch { /* best effort */ }
        }
    }

    // mod.io wants a .zip name of 100 characters at most.
    private static string ArchiveName(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        return (name.Length > 96 ? name[..96] : name) + ".zip";
    }

    private static async Task<string> ComputeMd5Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await MD5.HashDataAsync(stream, ct));
    }

    private static void AddFile(MultipartFormDataContent form, string field, string path)
    {
        var content = new StreamContent(File.OpenRead(path));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, field, Path.GetFileName(path));
    }

    // ─── Plumbing ────────────────────────────────────────────────────────────

    private static Uri Url(ModioCredentials auth, string path) => new(auth.BaseUrl, path);

    private async Task<T> GetAsync<T>(ModioCredentials auth, string path, JsonTypeInfo<T> type, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Url(auth, path));
        using var resp = await SendAsync(auth, req, ct);
        return await ReadAsync(resp, type, ct);
    }

    private async Task<List<T>> GetAllAsync<T>(ModioCredentials auth, string path, JsonTypeInfo<ModioPaged<T>> type, CancellationToken ct)
    {
        var separator = path.Contains('?') ? '&' : '?';
        var all = new List<T>();
        while (true)
        {
            var page = await GetAsync(auth, $"{path}{separator}_limit={PageSize}&_offset={all.Count}", type, ct);
            all.AddRange(page.Data);
            if (page.Data.Count < PageSize || all.Count >= page.ResultTotal) return all;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(ModioCredentials auth, HttpRequestMessage req, CancellationToken ct)
    {
        ThrowIfRateLimited();
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode) return resp;

        using (resp) throw await ToExceptionAsync(resp, req, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp, JsonTypeInfo<T> type, CancellationToken ct) =>
        await resp.Content.ReadFromJsonAsync(type, ct)
        ?? throw new ModioApiException((int)resp.StatusCode, 0, "Empty response from mod.io.");

    private void ThrowIfRateLimited()
    {
        TimeSpan wait;
        lock (_rateLock) wait = _blockedUntil - _time.GetUtcNow();
        if (wait > TimeSpan.Zero)
            throw new ModioApiException(429, 0, "mod.io rate limit: waiting before the next request.", retryAfter: wait);
    }

    private async Task<ModioApiException> ToExceptionAsync(HttpResponseMessage resp, HttpRequestMessage req, CancellationToken ct)
    {
        var status = (int)resp.StatusCode;
        ModioError? error = null;
        try
        {
            error = (await resp.Content.ReadFromJsonAsync(ModioJsonContext.Default.ModioErrorResponse, ct))?.Error;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Log.Debug($"Unreadable mod.io error body ({status}): {ex.Message}");
        }

        TimeSpan? retryAfter = null;
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delta = resp.Headers.RetryAfter?.Delta;
            retryAfter = delta is { } d && d > TimeSpan.Zero ? d : DefaultRetryAfter;
            lock (_rateLock) _blockedUntil = _time.GetUtcNow() + retryAfter.Value;
        }

        Log.Warning($"mod.io {req.Method} {req.RequestUri?.AbsolutePath} failed: {status} ref {error?.ErrorRef.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        return new ModioApiException(status, error?.ErrorRef ?? 0,
            error?.Message ?? resp.ReasonPhrase ?? "mod.io request failed.", error?.Errors, retryAfter);
    }
}

/// <summary>Streams a file and reports the bytes sent, for a real progress bar.</summary>
internal sealed class ProgressStreamContent(Stream stream, long length, IProgress<long> progress) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context)
    {
        var buffer = new byte[81920];
        long sent = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            sent += read;
            progress.Report(sent);
        }
    }

    protected override bool TryComputeLength(out long size)
    {
        size = length;
        return true;
    }
}
