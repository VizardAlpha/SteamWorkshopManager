using Microsoft.VisualStudio.TestTools.UnitTesting;
using Steamworks;
using WorkshopManager.Core.Steam;

namespace WorkshopManager.Tests;

// No Avalonia Application in tests, so LocalizationService.GetString echoes the key.
[TestClass]
public class SteamErrorMapperTests
{
    [TestMethod]
    [DataRow(EResult.k_EResultOK, "SteamOK")]
    [DataRow(EResult.k_EResultFail, "SteamFail")]
    [DataRow(EResult.k_EResultAccessDenied, "SteamAccessDenied")]
    [DataRow(EResult.k_EResultTimeout, "SteamTimeout")]
    [DataRow(EResult.k_EResultFileNotFound, "SteamFileNotFound")]
    [DataRow(EResult.k_EResultLimitExceeded, "SteamLimitExceeded")]
    [DataRow(EResult.k_EResultInvalidItemType, "SteamInvalidItemType")]
    public void GetErrorMessage_KnownResult_MapsToItsKey(EResult result, string expectedKey)
    {
        Assert.AreEqual(expectedKey, SteamErrorMapper.GetErrorMessage(result));
    }

    [TestMethod]
    [DataRow(EResult.k_EResultNone)]
    [DataRow((EResult)99999)]
    public void GetErrorMessage_UnmappedResult_FallsBackToUnknown(EResult result)
    {
        Assert.AreEqual("SteamUnknownError", SteamErrorMapper.GetErrorMessage(result));
    }

    [TestMethod]
    public void GetCreateItemErrorMessage_AccessDenied_ExplainsRefusedSubmissions()
    {
        Assert.AreEqual(
            "WorkshopSubmissionRefused",
            SteamErrorMapper.GetCreateItemErrorMessage(EResult.k_EResultAccessDenied));
    }

    [TestMethod]
    public void GetCreateItemErrorMessage_OtherResult_UsesGenericMapping()
    {
        Assert.AreEqual("SteamTimeout", SteamErrorMapper.GetCreateItemErrorMessage(EResult.k_EResultTimeout));
    }

    [TestMethod]
    public void GetTechnicalDescription_IncludesNameAndCode()
    {
        Assert.AreEqual(
            "Steam API Error: k_EResultAccessDenied (Code: 15)",
            SteamErrorMapper.GetTechnicalDescription(EResult.k_EResultAccessDenied));
    }
}
