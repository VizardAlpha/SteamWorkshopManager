using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Sessions;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Cache;
using SteamWorkshopManager.Services.Platforms.Modio;
using SteamWorkshopManager.Services.Steam;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class ModioPublisherTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpStatusCode> Status { get; set; } = _ => HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            var status = Status(request);
            var json = request.RequestUri.AbsolutePath.EndsWith("/me") ? """{"id":7,"username":"Tester"}""" : """{"id":9,"name":"Mod"}""";
            return new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? json : "{}", Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ListProgress : IProgress<UploadProgress>
    {
        public List<UploadProgress> Reports { get; } = [];
        public void Report(UploadProgress value) => Reports.Add(value);
    }

    private string _file = null!;
    private string _cacheRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.Combine(Path.GetTempPath(), $"swm-modio-publisher-{Guid.NewGuid():N}.bin");
        _cacheRoot = Path.Combine(Path.GetTempPath(), "swm-modio-publisher-cache", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_file)) File.Delete(_file);
        if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true);
    }

    private async Task<(ModioPublisher Publisher, RecordingHandler Handler)> CreateAsync()
    {
        var handler = new RecordingHandler();
        var client = new ModioApiClient(handler, TimeProvider.System);
        var account = new ModioAccount(client, _file);
        await account.SignInAsync("https://u-7.modapi.io/v1", "token");
        handler.Requests.Clear();

        var context = new SessionContext();
        context.Activate(new ModioSession { GameId = 5 });
        var cache = new CacheStore(_cacheRoot, enabled: true, limitBytes: null);
        await cache.Initialization;
        return (new ModioPublisher(client, account, context, cache), handler);
    }

    [TestMethod]
    public async Task Update_SendsOnlyWhatChangedInOrder()
    {
        var (publisher, handler) = await CreateAsync();
        var progress = new ListProgress();
        var media = new ModioMediaChange([], ["https://youtu.be/x"], [], ["old.png"], [], []);

        await publisher.UpdateAsync(new ModioModUpdate(9,
            new ModioModEdit(Summary: "Short", CommunityOptions: 1),
            TagsToAdd: ["Map"], TagsToRemove: ["Old"],
            LogoPath: null, Media: media, File: null), progress);

        CollectionAssert.AreEqual(
            new[]
            {
                "POST /v1/games/5/mods/9", "DELETE /v1/games/5/mods/9/tags", "POST /v1/games/5/mods/9/tags",
                "DELETE /v1/games/5/mods/9/media", "POST /v1/games/5/mods/9/media",
            },
            handler.Requests.Select(r => $"{r.Method} {r.Path}").ToArray());
        StringAssert.Contains(handler.Requests[0].Body, "Short");
        StringAssert.Contains(handler.Requests[0].Body, "community_options");
        StringAssert.Contains(handler.Requests[3].Body, "old.png");
        Assert.AreEqual(100, progress.Reports[^1].Percentage);
    }

    [TestMethod]
    public async Task Update_WithNewFolder_UploadsAZip()
    {
        var (publisher, handler) = await CreateAsync();
        var (folder, _) = CreateContent();
        try
        {
            await publisher.UpdateAsync(new ModioModUpdate(9, new ModioModEdit(), [], [],
                LogoPath: null, ModioMediaChange.None, new ModioFileUpload(folder, "1.2", "Fixes")));

            var upload = handler.Requests.Single(r => r.Path == "/v1/games/5/mods/9/files");
            StringAssert.Contains(upload.Body, "filename=mod-9.zip");
            StringAssert.Contains(upload.Body, "1.2");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Update_Failure_StillClosesTheProgressToast()
    {
        var (publisher, handler) = await CreateAsync();
        handler.Status = _ => HttpStatusCode.UnprocessableEntity;
        var progress = new ListProgress();

        await Assert.ThrowsExactlyAsync<ModioApiException>(() => publisher.UpdateAsync(
            new ModioModUpdate(9, new ModioModEdit(Name: "X"), [], [], null, ModioMediaChange.None, null), progress));

        Assert.AreEqual(100, progress.Reports[^1].Percentage);
    }

    [TestMethod]
    public async Task Create_OnlyCreatesTheMod()
    {
        var (publisher, handler) = await CreateAsync();
        var (folder, logo) = CreateContent();
        try
        {
            var created = await publisher.CreateAsync(new ModioNewMod("Name", "Summary", logo, null, Visible: true,
                Tags: ["Map"], HomepageUrl: "https://example.com", CommunityOptions: ModioCommunity.Comments));

            Assert.AreEqual(9, created.Id);
            Assert.AreEqual(1, handler.Requests.Count);
            Assert.AreEqual("POST /v1/games/5/mods", $"{handler.Requests[0].Method} {handler.Requests[0].Path}");
            StringAssert.Contains(handler.Requests[0].Body, "name=logo");
            StringAssert.Contains(handler.Requests[0].Body, "https://example.com");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Community_KeepsBitsTheFormDoesNotShow()
    {
        const int withDependencies = ModioCommunity.Comments | ModioCommunity.Dependencies;

        Assert.AreEqual(1024, ModioCommunity.With(withDependencies, ModioCommunity.Comments, false));
        Assert.AreEqual(withDependencies | ModioCommunity.Collections,
            ModioCommunity.With(withDependencies, ModioCommunity.Collections, true));
    }

    [TestMethod]
    public async Task Reads_ComeFromTheCacheUntilOurOwnWrite()
    {
        var (publisher, handler) = await CreateAsync();

        await publisher.GetModAsync(9, "rev1");
        await publisher.GetModAsync(9, "rev1");
        Assert.AreEqual(1, handler.Requests.Count);

        await publisher.UpdateAsync(new ModioModUpdate(9, new ModioModEdit(Name: "New"), [], [], null, ModioMediaChange.None, null));
        handler.Requests.Clear();

        await publisher.GetModAsync(9, "rev1");
        Assert.AreEqual(1, handler.Requests.Count);
    }

    private static (string Folder, string Logo) CreateContent()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"swm-modio-create-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.txt"), "content");
        var logo = Path.Combine(folder, "logo.png");
        File.WriteAllBytes(logo, [0x89, 0x50, 0x4E, 0x47]);
        return (folder, logo);
    }

    [TestMethod]
    [DataRow("", "Summary", null, null, "TitleRequired")]
    [DataRow("Name", " ", null, null, "ModioSummaryRequired")]
    [DataRow("Name", "Summary", null, "not a link", "ModioHomepageInvalid")]
    [DataRow("Name", "Summary", null, "https://example.com", null)]
    [DataRow("Name", "Summary", null, null, null)]
    public void Rules_ReportFirstBrokenRule(string name, string summary, string? description, string? homepage, string? expected)
    {
        Assert.AreEqual(expected, ModioModRules.Validate(name, summary, description, homepage));
    }

    [TestMethod]
    public void Rules_TextLimits()
    {
        Assert.IsNull(ModioModRules.Validate("Name", new string('a', 250), null, null));
        Assert.AreEqual("ModioSummaryTooLong", ModioModRules.Validate("Name", new string('a', 251), null, null));
        Assert.AreEqual("ModioDescriptionTooLong", ModioModRules.Validate("Name", "S", new string('a', 50_001), null));
    }

    [TestMethod]
    [DataRow("logo.gif", "ModioImageFormat")]
    [DataRow("missing.png", "ModioImageTooLarge")]
    public void Rules_ImageFormatAndSize(string path, string expected)
    {
        Assert.AreEqual(expected, ModioModRules.ValidateImage(path));
    }
}
