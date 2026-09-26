using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamWorkshopManager.Helpers;

namespace SteamWorkshopManager.Tests;

[TestClass]
public class AtomicFileTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "swm-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    [TestMethod]
    public void WriteAllText_ReplacesContentAndLeavesNoTempFile()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "old content that is longer");

        AtomicFile.WriteAllText(path, "{\"é\":1}");

        Assert.AreEqual("{\"é\":1}", File.ReadAllText(path));
        Assert.AreSequenceEqual(new[] { path }, Directory.GetFiles(_dir));
    }

    [TestMethod]
    public void WriteAllText_WritesUtf8WithoutBom()
    {
        var path = Path.Combine(_dir, "a.json");

        AtomicFile.WriteAllText(path, "x");

        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes("x"), File.ReadAllBytes(path));
    }

    [TestMethod]
    public async Task WriteAllTextAsync_ConcurrentWritersNeverCorruptTheFile()
    {
        var path = Path.Combine(_dir, "session.json");
        var payloads = Enumerable.Range(0, 50).Select(i => new string((char)('a' + i % 26), 1000 + i)).ToList();

        await Task.WhenAll(payloads.Select(p => Task.Run(() => AtomicFile.WriteAllTextAsync(path, p))));

        CollectionAssert.Contains(payloads, File.ReadAllText(path));
        Assert.AreSequenceEqual(new[] { path }, Directory.GetFiles(_dir));
    }
}
