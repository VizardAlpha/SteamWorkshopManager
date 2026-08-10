using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Core.Steam;
using SteamWorkshopManager.Services.Steam;

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

    /// <summary>
    /// Pins the endpoint literals once, so the tests below can build their URLs
    /// from <see cref="SteamUrls"/> without losing coverage on the actual values.
    /// </summary>
    [TestMethod]
    public void SteamUrls_BuildTheExpectedEndpoints()
    {
        Assert.AreEqual("https://store.steampowered.com/api/appdetails?appids=440", SteamUrls.AppDetails(440));
        Assert.AreEqual("https://steamcommunity.com/app/440/workshop/", SteamUrls.WorkshopPage(440));
    }

    [TestMethod]
    public async Task ValidateAsync_WorkshopCategoryPresent_SkipsTheWorkshopProbe()
    {
        const uint appId = 1162750;
        var (validator, handler) = CreateValidator(
            (SteamUrls.AppDetails(appId), StoreResponse(appId, "Songs of Syx", withWorkshopCategory: true)));

        var result = await validator.ValidateAsync(appId);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("Songs of Syx", result.GameName);
        Assert.AreSequenceEqual(new[] { SteamUrls.AppDetails(appId) }, handler.RequestedUrls);
    }

    [TestMethod]
    public async Task ValidateAsync_WorkshopCategoryMissingButPageStaysOnWorkshop_ReturnsValid()
    {
        const uint appId = 1022980;
        var (validator, handler) = CreateValidator(
            (SteamUrls.AppDetails(appId), StoreResponse(appId, "Ostranauts", withWorkshopCategory: false)),
            (SteamUrls.WorkshopPage(appId), StubResponse.NoRedirect));

        var result = await validator.ValidateAsync(appId);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(appId, result.AppId);
        Assert.AreEqual("Ostranauts", result.GameName);
        Assert.AreSequenceEqual(
            new[] { SteamUrls.AppDetails(appId), SteamUrls.WorkshopPage(appId) },
            handler.RequestedUrls);
    }

    [TestMethod]
    public async Task ValidateAsync_WorkshopPageRedirectsToGameHub_ReturnsNoWorkshop()
    {
        const uint appId = 1086940;
        var (validator, _) = CreateValidator(
            (SteamUrls.AppDetails(appId), StoreResponse(appId, "Baldur's Gate 3", withWorkshopCategory: false)),
            (SteamUrls.WorkshopPage(appId), StubResponse.RedirectedTo($"https://steamcommunity.com/app/{appId}/")));

        var result = await validator.ValidateAsync(appId);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("NoWorkshop", result.ErrorKey);
        Assert.AreEqual("Baldur's Gate 3", result.GameName);
    }

    [TestMethod]
    public async Task ValidateAsync_WorkshopProbeReturnsError_ReturnsNoWorkshop()
    {
        const uint appId = 1086940;
        var (validator, _) = CreateValidator(
            (SteamUrls.AppDetails(appId), StoreResponse(appId, "Baldur's Gate 3", withWorkshopCategory: false)),
            (SteamUrls.WorkshopPage(appId), StubResponse.WithStatus(HttpStatusCode.NotFound)));

        var result = await validator.ValidateAsync(appId);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("NoWorkshop", result.ErrorKey);
    }

    [TestMethod]
    public async Task ValidateAsync_WorkshopProbeRequestFails_ReturnsNetworkError()
    {
        const uint appId = 1022980;
        var (validator, _) = CreateValidator(
            (SteamUrls.AppDetails(appId), StoreResponse(appId, "Ostranauts", withWorkshopCategory: false)));

        var result = await validator.ValidateAsync(appId);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("NetworkError", result.ErrorKey);
    }

    private static (AppIdValidator Validator, StubHttpMessageHandler Handler) CreateValidator(
        params (string Url, StubResponse Response)[] stubs)
    {
        var handler = new StubHttpMessageHandler(stubs.ToDictionary(s => s.Url, s => s.Response));
        return (new AppIdValidator(new HttpClient(handler)), handler);
    }

    private static StubResponse StoreResponse(uint appId, string name, bool withWorkshopCategory)
    {
        var categories = withWorkshopCategory
            ? "[{\"id\":30,\"description\":\"Steam Workshop\"}]"
            : "[]";
        return StubResponse.NoRedirect with
        {
            Body = $"{{\"{appId}\":{{\"success\":true,\"data\":{{\"name\":\"{name}\",\"categories\":{categories}}}}}}}",
        };
    }

    /// <param name="FinalUrl">
    /// URL the response is reported as coming from null means "no redirect".
    /// Mirrors HttpClientHandler, which rewrites RequestUri while following 3xx.
    /// </param>
    private sealed record StubResponse(
        string Body = "",
        string? FinalUrl = null,
        HttpStatusCode Status = HttpStatusCode.OK)
    {
        public static StubResponse NoRedirect => new();

        public static StubResponse RedirectedTo(string finalUrl) => new(FinalUrl: finalUrl);

        public static StubResponse WithStatus(HttpStatusCode status) => new(Status: status);
    }

    private sealed class StubHttpMessageHandler(IReadOnlyDictionary<string, StubResponse> responses)
        : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestedUrls.Add(url);

            if (!responses.TryGetValue(url, out var stub))
                throw new HttpRequestException($"No stub response configured for {url}");

            return Task.FromResult(new HttpResponseMessage(stub.Status)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, stub.FinalUrl ?? url),
                Content = new StringContent(stub.Body, Encoding.UTF8),
            });
        }
    }
}
