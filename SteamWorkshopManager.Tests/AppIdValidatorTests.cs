using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Steam;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class AppIdValidatorTests
{
    [TestMethod]
    [DataRow("440", 440U)]
    [DataRow("  570  ", 570U)]
    [DataRow("https://store.steampowered.com/app/620/Portal_2/", 620U)]
    [DataRow("https://steamcommunity.com/app/2555430/workshop/", 2555430U)]
    [DataRow("steam://store/294100", 294100U)]
    public void TryParseAppId_Valid_ReturnsTrueAndId(string input, uint expectedId)
    {
        var ok = AppIdValidator.TryParseAppId(input, out var appId);
        Assert.IsTrue(ok);
        Assert.AreEqual(expectedId, appId);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("abc")]
    [DataRow("0")]
    public void TryParseAppId_Invalid_ReturnsFalse(string input)
    {
        var ok = AppIdValidator.TryParseAppId(input, out var appId);
        Assert.IsFalse(ok);
        Assert.AreEqual(0U, appId);
    }

    [TestMethod]
    public async Task ValidateAsync_WorkshopCategoryPresent_UsesStoreMetadata()
    {
        const uint appId = 1162750;
        var handler = new StubHttpMessageHandler(new Dictionary<string, string>
        {
            [StoreUrl(appId)] = StoreResponse(appId, "Songs of Syx", includeWorkshopCategory: true),
        });
        var validator = new AppIdValidator(new HttpClient(handler));

        var result = await validator.ValidateAsync(appId);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("Songs of Syx", result.GameName);
        CollectionAssert.AreEqual(new[] { StoreUrl(appId) }, handler.RequestedUrls);
    }

    [TestMethod]
    public async Task ValidateAsync_WorkshopCategoryMissingButWorkshopPageExists_ReturnsValid()
    {
        const uint appId = 1022980;
        var handler = new StubHttpMessageHandler(new Dictionary<string, string>
        {
            [StoreUrl(appId)] = StoreResponse(appId, "Ostranauts", includeWorkshopCategory: false),
            [WorkshopUrl(appId)] = "<script>window.SSR.loaderData = [];</script>",
        });
        var validator = new AppIdValidator(new HttpClient(handler));

        var result = await validator.ValidateAsync(appId);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(appId, result.AppId);
        Assert.AreEqual("Ostranauts", result.GameName);
        CollectionAssert.AreEqual(
            new[] { StoreUrl(appId), WorkshopUrl(appId) },
            handler.RequestedUrls);
    }

    [TestMethod]
    public async Task ValidateAsync_WorkshopCategoryMissingAndNoWorkshopPage_ReturnsNoWorkshop()
    {
        const uint appId = 1086940;
        var handler = new StubHttpMessageHandler(new Dictionary<string, string>
        {
            [StoreUrl(appId)] = StoreResponse(appId, "Baldur's Gate 3", includeWorkshopCategory: false),
            [WorkshopUrl(appId)] = "<title>Baldur's Gate 3 :: Steam Community</title>",
        });
        var validator = new AppIdValidator(new HttpClient(handler));

        var result = await validator.ValidateAsync(appId);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("NoWorkshop", result.ErrorKey);
        Assert.AreEqual("Baldur's Gate 3", result.GameName);
    }

    [TestMethod]
    public async Task ValidateAsync_WorkshopFallbackRequestFails_ReturnsNetworkError()
    {
        const uint appId = 1022980;
        var handler = new StubHttpMessageHandler(new Dictionary<string, string>
        {
            [StoreUrl(appId)] = StoreResponse(appId, "Ostranauts", includeWorkshopCategory: false),
        });
        var validator = new AppIdValidator(new HttpClient(handler));

        var result = await validator.ValidateAsync(appId);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("NetworkError", result.ErrorKey);
    }

    private static string StoreUrl(uint appId) =>
        $"https://store.steampowered.com/api/appdetails?appids={appId}";

    private static string WorkshopUrl(uint appId) =>
        $"https://steamcommunity.com/app/{appId}/workshop/";

    private static string StoreResponse(uint appId, string name, bool includeWorkshopCategory)
    {
        var categories = includeWorkshopCategory
            ? "[{\"id\":30,\"description\":\"Steam Workshop\"}]"
            : "[]";
        return $"{{\"{appId}\":{{\"success\":true,\"data\":{{\"name\":\"{name}\",\"categories\":{categories}}}}}}}";
    }

    private sealed class StubHttpMessageHandler(IReadOnlyDictionary<string, string> responses)
        : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestedUrls.Add(url);

            if (!responses.TryGetValue(url, out var content))
                throw new HttpRequestException($"No stub response configured for {url}");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8),
            });
        }
    }
}
