using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Helpers;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class FormattersTests
{
    // Decimal cases are culture-dependent (FR uses ','), so only assert exact
    // text on non-decimal branches; assert the unit on decimal branches.
    [TestMethod]
    [DataRow(0L, "0 B")]
    [DataRow(-5L, "0 B")]
    [DataRow(512L, "512 B")]
    [DataRow(1023L, "1023 B")]
    public void Bytes_NoDecimalCases(long input, string expected)
    {
        Assert.AreEqual(expected, Formatters.Bytes(input));
    }

    [TestMethod]
    public void Bytes_Kilobytes_UsesKbUnit()
    {
        StringAssert.Contains(Formatters.Bytes(2048), "KB");
    }

    [TestMethod]
    public void Bytes_Megabytes_UsesMbUnit()
    {
        StringAssert.Contains(Formatters.Bytes(5L * 1024 * 1024), "MB");
    }

    [TestMethod]
    [DataRow(0L, "0")]
    [DataRow(-5L, "0")]
    [DataRow(1L, "1")]
    [DataRow(999L, "999")]
    public void CompactNumber_NoDecimalCases(long input, string expected)
    {
        Assert.AreEqual(expected, Formatters.CompactNumber(input));
    }

    [TestMethod]
    public void CompactNumber_Thousands_UsesKSuffix()
    {
        StringAssert.Contains(Formatters.CompactNumber(1500), "K");
    }
}
