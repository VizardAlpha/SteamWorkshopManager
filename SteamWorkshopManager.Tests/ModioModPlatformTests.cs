using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Platforms.Modio;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ModioModPlatformTests
{
    private sealed class QueueHandler : HttpMessageHandler
    {
        public Queue<(HttpStatusCode Status, string Body)> Responses { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var (status, body) = Responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private string _file = null!;

    [TestInitialize]
    public void Setup() => _file = Path.Combine(Path.GetTempPath(), $"swm-modio-platform-{Guid.NewGuid():N}.bin");

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    private async Task<(ModioModPlatform Platform, ModioAccount Account, QueueHandler Handler)> CreateSignedInAsync()
    {
        var handler = new QueueHandler();
        var client = new ModioApiClient(handler, TimeProvider.System);
        var account = new ModioAccount(client, _file);
        handler.Responses.Enqueue((HttpStatusCode.OK, """{"id":7,"username":"Tester"}"""));
        await account.SignInAsync("https://u-7.modapi.io/v1", "token");

        var context = new SessionContext();
        context.Activate(new ModioSession { GameId = 5 });
        return (new ModioModPlatform(client, account, context), account, handler);
    }

    [TestMethod]
    public async Task MyMods_MapsFieldsAndSkipsDeletedMods()
    {
        var (platform, _, handler) = await CreateSignedInAsync();
        handler.Responses.Enqueue((HttpStatusCode.OK, """
            {"data":[
              {"id":11,"status":1,"visible":1,"name":"Live","date_added":1700000000,"date_updated":1700000500,
               "logo":{"thumb_320x180":"https://img/logo.png"},"tags":[{"name":"Map"}],
               "stats":{"subscribers_total":12},"modfile":{"filesize":2048},"submitted_by":{"id":7}},
              {"id":12,"status":3,"visible":0,"name":"Gone"},
              {"id":13,"status":1,"visible":0,"name":"Team","submitted_by":{"id":99}}
            ],"result_total":3}
            """));

        var items = await platform.GetMyModsAsync();

        Assert.AreEqual(2, items.Count);
        var live = items[0];
        Assert.AreEqual(11UL, live.Id);
        Assert.AreEqual(VisibilityType.Public, live.Visibility);
        Assert.AreEqual("https://img/logo.png", live.PreviewImageUrl);
        Assert.AreEqual("Map", live.Tags[0].Name);
        Assert.AreEqual(12UL, live.SubscriberCount);
        Assert.AreEqual(2048, live.FileSize);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000500).UtcDateTime, live.UpdatedAt);
        Assert.IsTrue(live.IsOwner);

        Assert.AreEqual(VisibilityType.Private, items[1].Visibility);
        Assert.IsFalse(items[1].IsOwner);
    }

    [TestMethod]
    public async Task RefusedToken_SignsOutAndRaisesExpired()
    {
        var (platform, account, handler) = await CreateSignedInAsync();
        var expired = 0;
        account.Expired += () => expired++;
        handler.Responses.Enqueue((HttpStatusCode.Unauthorized, """{"error":{"code":401,"error_ref":11005,"message":"Expired"}}"""));

        await Assert.ThrowsExactlyAsync<ModioApiException>(() => platform.GetMyModsAsync());

        Assert.AreEqual(1, expired);
        Assert.IsFalse(account.IsSignedIn);
        Assert.IsFalse(platform.IsReady);
        Assert.IsFalse(File.Exists(_file));
    }

    [TestMethod]
    public async Task UnknownMod_ReturnsNull()
    {
        var (platform, _, handler) = await CreateSignedInAsync();
        handler.Responses.Enqueue((HttpStatusCode.NotFound, """{"error":{"code":404,"error_ref":15022,"message":"Not found"}}"""));

        Assert.IsNull(await platform.GetModAsync(42));
    }

    [TestMethod]
    public async Task NotReady_OnSteamSession()
    {
        var (_, account, _) = await CreateSignedInAsync();
        var context = new SessionContext();
        context.Activate(new SteamSession { AppId = 1 });

        var platform = new ModioModPlatform(new ModioApiClient(new QueueHandler(), TimeProvider.System), account, context);

        Assert.IsFalse(platform.IsReady);
        Assert.AreEqual(7UL, platform.AccountId);
    }
}
