using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Services.Platforms.Modio;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ModioApiClientTests
{
    private static readonly ModioCredentials Auth = new(new Uri("https://u-1.modapi.io/v1/"), "secret-token");

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Sent(HttpMethod Method, Uri Uri, string? Authorization, string? ContentRange, long BodyLength, string Body);

    /// <summary>Answers each request with the next queued response and records what was sent.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Queue<Func<HttpResponseMessage>> Responses { get; } = new();
        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            Requests.Add(new Sent(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentRange?.ToString(), body.Length, Encoding.UTF8.GetString(body)));
            return Responses.Dequeue()();
        }
    }

    private static Func<HttpResponseMessage> Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (ModioApiClient Client, FakeHandler Handler, ManualTime Time) Create(long threshold = ModioApiClient.MultipartThreshold, int partSize = ModioApiClient.PartSize)
    {
        var handler = new FakeHandler();
        var time = new ManualTime();
        return (new ModioApiClient(handler, time, threshold, partSize), handler, time);
    }

    [TestMethod]
    public async Task GetMe_SendsBearerTokenToUserHost()
    {
        var (client, handler, _) = Create();
        handler.Responses.Enqueue(Json("""{"id":7,"username":"Viz","name_id":"viz"}"""));

        var me = await client.GetMeAsync(Auth);

        Assert.AreEqual(7, me.Id);
        Assert.AreEqual("Viz", me.Username);
        Assert.AreEqual("https://u-1.modapi.io/v1/me", handler.Requests[0].Uri.ToString());
        Assert.AreEqual("Bearer secret-token", handler.Requests[0].Authorization);
    }

    [TestMethod]
    public async Task GetMyMods_FollowsPagination()
    {
        var (client, handler, _) = Create();
        var firstPage = string.Join(",", Enumerable.Range(1, 100).Select(i => $$"""{"id":{{i}},"name":"m{{i}}"}"""));
        handler.Responses.Enqueue(Json($$"""{"data":[{{firstPage}}],"result_total":101}"""));
        handler.Responses.Enqueue(Json("""{"data":[{"id":101,"name":"last"}],"result_total":101}"""));

        var mods = await client.GetMyModsAsync(Auth, 5);

        Assert.AreEqual(101, mods.Count);
        Assert.AreEqual("last", mods[^1].Name);
        StringAssert.Contains(handler.Requests[0].Uri.Query, "game_id=5");
        StringAssert.Contains(handler.Requests[1].Uri.Query, "_offset=100");
    }

    [TestMethod]
    public async Task Error_CarriesStatusRefAndFieldErrors()
    {
        var (client, handler, _) = Create();
        handler.Responses.Enqueue(Json("""{"error":{"code":422,"error_ref":13009,"message":"Invalid","errors":{"name":"Too long"}}}""",
            HttpStatusCode.UnprocessableEntity));

        var ex = await Assert.ThrowsExactlyAsync<ModioApiException>(() => client.GetMeAsync(Auth));

        Assert.AreEqual(422, ex.StatusCode);
        Assert.AreEqual(13009, ex.ErrorRef);
        Assert.AreEqual("Too long", ex.FieldErrors!["name"]);
        Assert.IsFalse(ex.Message.Contains("secret-token"));
    }

    [TestMethod]
    public async Task Unauthorized_IsAnAuthError()
    {
        var (client, handler, _) = Create();
        handler.Responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsExactlyAsync<ModioApiException>(() => client.GetMeAsync(Auth));

        Assert.IsTrue(ex.IsAuthError);
    }

    [TestMethod]
    public async Task RateLimited_BlocksCallsUntilRetryAfter()
    {
        var (client, handler, time) = Create();
        handler.Responses.Enqueue(() =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return resp;
        });

        var first = await Assert.ThrowsExactlyAsync<ModioApiException>(() => client.GetMeAsync(Auth));
        Assert.AreEqual(TimeSpan.FromSeconds(30), first.RetryAfter);

        // Blocked locally: nothing reaches mod.io.
        var second = await Assert.ThrowsExactlyAsync<ModioApiException>(() => client.GetMeAsync(Auth));
        Assert.IsTrue(second.IsRateLimited);
        Assert.AreEqual(1, handler.Requests.Count);

        time.Now += TimeSpan.FromSeconds(31);
        handler.Responses.Enqueue(Json("""{"id":1}"""));
        await client.GetMeAsync(Auth);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task RateLimited_ZeroRetryAfterWaitsAMinute()
    {
        var (client, handler, _) = Create();
        handler.Responses.Enqueue(() =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return resp;
        });

        var ex = await Assert.ThrowsExactlyAsync<ModioApiException>(() => client.GetMeAsync(Auth));

        Assert.AreEqual(TimeSpan.FromSeconds(60), ex.RetryAfter);
    }

    [TestMethod]
    public async Task EditMod_WithNothingToChange_SendsNothing()
    {
        var (client, handler, _) = Create();

        Assert.IsNull(await client.EditModAsync(Auth, 5, 9, new ModioModEdit()));
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task EditMod_PostsOnlyChangedFields()
    {
        var (client, handler, _) = Create();
        handler.Responses.Enqueue(Json("""{"id":9,"name":"New","visible":0}"""));

        var mod = await client.EditModAsync(Auth, 5, 9, new ModioModEdit(Name: "New", Visible: false));

        Assert.AreEqual("New", mod!.Name);
        var sent = handler.Requests[0];
        Assert.AreEqual(HttpMethod.Post, sent.Method);
        Assert.AreEqual("/v1/games/5/mods/9", sent.Uri.AbsolutePath);
        StringAssert.Contains(sent.Body, "name=name");
        Assert.IsFalse(sent.Body.Contains("name=summary"));
    }

    [TestMethod]
    public async Task UploadModfile_SmallFile_SendsOneRequestWithMd5()
    {
        var (client, handler, _) = Create();
        var zip = TempFile(1000);
        try
        {
            handler.Responses.Enqueue(Json("""{"id":3,"filesize":1000}"""));

            var file = await client.UploadModfileAsync(Auth, 5, 9, zip, "1.0", null);

            Assert.AreEqual(3, file.Id);
            Assert.AreEqual(1, handler.Requests.Count);
            StringAssert.Contains(handler.Requests[0].Body, "name=filedata");
            StringAssert.Contains(handler.Requests[0].Body, "name=filehash");
        }
        finally { File.Delete(zip); }
    }

    [TestMethod]
    public async Task UploadModfile_LargeFile_SendsExactPartsThenCompletes()
    {
        var (client, handler, _) = Create(threshold: 100, partSize: 400);
        var zip = TempFile(1000);
        try
        {
            handler.Responses.Enqueue(Json("""{"upload_id":"u-123","status":0}"""));
            for (var i = 0; i < 3; i++) handler.Responses.Enqueue(Json("{}"));
            handler.Responses.Enqueue(Json("""{"upload_id":"u-123","status":1}"""));
            handler.Responses.Enqueue(Json("""{"id":3,"filesize":1000}"""));

            await client.UploadModfileAsync(Auth, 5, 9, zip, null, null);

            var parts = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
            CollectionAssert.AreEqual(new long[] { 400, 400, 200 }, parts.Select(p => p.BodyLength).ToArray());
            Assert.AreEqual("bytes 800-999/1000", parts[^1].ContentRange);
            StringAssert.Contains(parts[0].Uri.Query, "upload_id=u-123");
            Assert.AreEqual("/v1/games/5/mods/9/files/multipart/complete", handler.Requests[4].Uri.AbsolutePath);
            StringAssert.Contains(handler.Requests[5].Body, "name=upload_id");
            Assert.IsFalse(handler.Requests[5].Body.Contains("name=filedata"));
        }
        finally { File.Delete(zip); }
    }

    [TestMethod]
    public async Task Modfiles_ListNewestFirstAndMakeOneLive()
    {
        var (client, handler, _) = Create();
        handler.Responses.Enqueue(Json("""{"data":[{"id":3,"version":"1.1"},{"id":2,"version":"1.0"}],"result_total":2}"""));
        handler.Responses.Enqueue(Json("""{"id":2,"version":"1.0"}"""));

        var files = await client.GetModfilesAsync(Auth, 5, 9);
        var live = await client.EditModfileAsync(Auth, 5, 9, 2, active: true);

        CollectionAssert.AreEqual(new long[] { 3, 2 }, files.Select(f => f.Id).ToArray());
        StringAssert.Contains(handler.Requests[0].Uri.Query, "_sort=-date_added");
        Assert.AreEqual(HttpMethod.Put, handler.Requests[1].Method);
        Assert.AreEqual("/v1/games/5/mods/9/files/2", handler.Requests[1].Uri.AbsolutePath);
        Assert.AreEqual("active=true", handler.Requests[1].Body);
        Assert.AreEqual(2, live.Id);
    }

    [TestMethod]
    public async Task Download_ChecksMd5AndKeepsTheTokenAway()
    {
        var (client, handler, _) = Create();
        var bytes = Encoding.UTF8.GetBytes("zip content");
        var md5 = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes));
        handler.Responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var target = Path.Combine(Path.GetTempPath(), $"swm-modio-dl-{Guid.NewGuid():N}.zip");
        try
        {
            await client.DownloadAsync("https://binary.modcdn.io/mods/1/file.zip", target, md5);

            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(target));
            Assert.IsNull(handler.Requests[0].Authorization);
            Assert.IsFalse(File.Exists(target + ".part"));
        }
        finally
        {
            File.Delete(target);
        }
    }

    [TestMethod]
    public async Task Download_WrongMd5_LeavesNothingBehind()
    {
        var (client, handler, _) = Create();
        handler.Responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        var target = Path.Combine(Path.GetTempPath(), $"swm-modio-dl-{Guid.NewGuid():N}.zip");

        await Assert.ThrowsExactlyAsync<ModioApiException>(() =>
            client.DownloadAsync("https://binary.modcdn.io/mods/1/file.zip", target, "00000000000000000000000000000000"));

        Assert.IsFalse(File.Exists(target));
        Assert.IsFalse(File.Exists(target + ".part"));
    }

    [TestMethod]
    public async Task Dependencies_ListAddAndRemoveById()
    {
        var (client, handler, _) = Create();
        handler.Responses.Enqueue(Json("""{"data":[{"mod_id":12,"name":"Core lib","dependency_depth":0}],"result_total":1}"""));
        handler.Responses.Enqueue(Json("""{"code":201,"message":"ok"}"""));
        handler.Responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.NoContent));

        var dependencies = await client.GetDependenciesAsync(Auth, 5, 9);
        await client.AddDependenciesAsync(Auth, 5, 9, [34]);
        await client.DeleteDependenciesAsync(Auth, 5, 9, [12]);

        Assert.AreEqual(12, dependencies[0].ModId);
        Assert.AreEqual("Core lib", dependencies[0].Name);
        StringAssert.Contains(handler.Requests[0].Uri.Query, "recursive=false");
        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        Assert.AreEqual("dependencies%5B%5D=34", handler.Requests[1].Body);
        Assert.AreEqual(HttpMethod.Delete, handler.Requests[2].Method);
        Assert.AreEqual("dependencies%5B%5D=12", handler.Requests[2].Body);
    }

    private static string TempFile(int size)
    {
        var path = Path.Combine(Path.GetTempPath(), $"swm-modio-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, Enumerable.Range(0, size).Select(i => (byte)i).ToArray());
        return path;
    }
}
