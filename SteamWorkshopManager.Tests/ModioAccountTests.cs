using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Services.Platforms.Modio;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ModioAccountTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private string _file = null!;

    [TestInitialize]
    public void Setup() => _file = Path.Combine(Path.GetTempPath(), $"swm-modio-account-{Guid.NewGuid():N}.bin");

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    private ModioAccount Create(StubHandler handler) =>
        new(new ModioApiClient(handler, TimeProvider.System), _file);

    [TestMethod]
    public async Task SignIn_ChecksTokenThenPersistsIt()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":42,"username":"VizardAlpha"}""");
        var account = Create(handler);
        var changes = 0;
        account.Changed += () => changes++;

        await account.SignInAsync("https://u-42.modapi.io/v1", " token ");

        Assert.IsTrue(account.IsSignedIn);
        Assert.AreEqual("VizardAlpha", account.Username);
        Assert.AreEqual(1, changes);

        var restored = Create(handler);
        Assert.IsTrue(restored.IsSignedIn);
        Assert.AreEqual(42, restored.UserId);
        Assert.AreEqual("token", restored.Credentials!.Token);
        Assert.AreEqual("https://u-42.modapi.io/v1/", restored.Credentials.BaseUrl.ToString());
    }

    [TestMethod]
    public async Task SignIn_RejectsForeignApiPathWithoutCallingIt()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var account = Create(handler);

        var ex = await Assert.ThrowsExactlyAsync<ModioSignInException>(
            () => account.SignInAsync("https://example.com/v1", "token"));

        Assert.AreEqual("ModioApiPathInvalid", ex.MessageKey);
        Assert.AreEqual(0, handler.Calls);
        Assert.IsFalse(File.Exists(_file));
    }

    [TestMethod]
    public async Task SignIn_RefusedTokenIsNotKept()
    {
        var account = Create(new StubHandler(HttpStatusCode.Unauthorized, """{"error":{"code":401,"error_ref":11005,"message":"No"}}"""));

        var ex = await Assert.ThrowsExactlyAsync<ModioSignInException>(
            () => account.SignInAsync("https://u-1.modapi.io/v1", "bad"));

        Assert.AreEqual("ModioTokenRejected", ex.MessageKey);
        Assert.IsFalse(account.IsSignedIn);
        Assert.IsFalse(File.Exists(_file));
    }

    [TestMethod]
    public async Task SignOut_ForgetsStoredToken()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":1,"username":"Someone"}""");
        var account = Create(handler);
        await account.SignInAsync("https://u-1.modapi.io/v1", "token");

        account.SignOut();

        Assert.IsFalse(account.IsSignedIn);
        Assert.IsFalse(File.Exists(_file));
        Assert.IsFalse(Create(handler).IsSignedIn);
    }

    [TestMethod]
    public void TagCatalog_SkipsAdminGroupsAndMarksDropdowns()
    {
        var catalog = ModioTagCatalog.From([
            new ModioTagOption { Name = "Type", Type = "dropdown", Tags = ["Map", "Skin"] },
            new ModioTagOption { Name = "Theme", Type = "checkboxes", Tags = ["Dark", "Fun"] },
            new ModioTagOption { Name = "Featured", Type = "checkboxes", Tags = ["Yes"], Locked = true },
            new ModioTagOption { Name = "Internal", Type = "checkboxes", Tags = ["x"], Hidden = true },
        ]);

        CollectionAssert.AreEquivalent(new[] { "Type", "Theme" }, catalog.TagsByCategory.Keys);
        CollectionAssert.AreEqual(new[] { "Type" }, catalog.DropdownCategories);
        CollectionAssert.AreEqual(new[] { "Map", "Skin" }, catalog.TagsByCategory["Type"]);
    }
}
