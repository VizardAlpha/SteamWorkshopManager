using System.IO.Compression;
using WorkshopManager.Core.Workshop;
using WorkshopManager.Models;
using WorkshopManager.Services.Steam;
using WorkshopManager.Services.Workshop;

namespace WorkshopManager.Tests;

[TestClass]
public class BulkDownloadTests
{
    private static readonly DateTime Updated = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static WorkshopItem Mod(ulong id, string title, long size = 100, DateTime? updated = null) =>
        new() { Id = id, Title = title, FileSize = size, UpdatedAt = updated ?? Updated };

    private static string NewFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "swm-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ─── Store ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Store_ZipsAFolder_AndTheBackupIsThenFoundAsCurrent()
    {
        var root = NewFolder();
        try
        {
            var content = Directory.CreateDirectory(Path.Combine(root, "content")).FullName;
            File.WriteAllText(Path.Combine(content, "mod.pak"), "data");
            Directory.CreateDirectory(Path.Combine(content, "sub"));
            File.WriteAllText(Path.Combine(content, "sub", "info.json"), "{}");
            var backups = Path.Combine(root, "backups");
            var mod = Mod(42, "My: Mod?");

            Assert.IsNull(ModBackupStore.FindCurrent(backups, mod));

            var zip = await ModBackupStore.StoreAsync(content, backups, mod, CancellationToken.None);

            Assert.AreEqual(zip, ModBackupStore.FindCurrent(backups, mod));
            StringAssert.Contains(Path.GetFileName(zip), "_42_");
            using var archive = ZipFile.OpenRead(zip);
            CollectionAssert.AreEquivalent(
                new[] { "mod.pak", "sub/info.json" },
                archive.Entries.Select(e => e.FullName.Replace('\\', '/')).Where(n => !n.EndsWith('/')).ToArray());
            Assert.AreEqual(0, Directory.GetFiles(backups, "*.part").Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Store_ContentThatIsAlreadyOneZip_IsCopiedNotZippedAgain()
    {
        var root = NewFolder();
        try
        {
            var content = Directory.CreateDirectory(Path.Combine(root, "content")).FullName;
            var original = Path.Combine(content, "packed.zip");
            using (var archive = ZipFile.Open(original, ZipArchiveMode.Create))
                archive.CreateEntry("inside.txt");

            var zip = await ModBackupStore.StoreAsync(content, Path.Combine(root, "backups"), Mod(1, "A"), CancellationToken.None);

            CollectionAssert.AreEqual(File.ReadAllBytes(original), File.ReadAllBytes(zip));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Store_ASingleFile_IsZipped()
    {
        var root = NewFolder();
        try
        {
            var legacy = Path.Combine(root, "legacy.bin");
            File.WriteAllText(legacy, "old style item");

            var zip = await ModBackupStore.StoreAsync(legacy, Path.Combine(root, "backups"), Mod(1, "A"), CancellationToken.None);

            using var archive = ZipFile.OpenRead(zip);
            Assert.AreEqual("legacy.bin", archive.Entries.Single().FullName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Store_ANewerVersionReplacesTheOlderZip_EvenAfterARename()
    {
        var root = NewFolder();
        try
        {
            var content = Directory.CreateDirectory(Path.Combine(root, "content")).FullName;
            File.WriteAllText(Path.Combine(content, "a.pak"), "v1");
            var backups = Path.Combine(root, "backups");

            var old = await ModBackupStore.StoreAsync(content, backups, Mod(7, "Old title"), CancellationToken.None);
            var other = await ModBackupStore.StoreAsync(content, backups, Mod(8, "Other mod"), CancellationToken.None);

            var newer = Mod(7, "New title", updated: Updated.AddDays(1));
            Assert.IsNull(ModBackupStore.FindCurrent(backups, newer));
            var fresh = await ModBackupStore.StoreAsync(content, backups, newer, CancellationToken.None);

            Assert.IsFalse(File.Exists(old));
            Assert.IsTrue(File.Exists(fresh));
            Assert.IsTrue(File.Exists(other));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void FreeSpace_IsReadEvenWhenTheFolderDoesNotExistYet()
    {
        var root = NewFolder();
        try
        {
            var free = ModBackupStore.FreeSpace(Path.Combine(root, "not", "created", "yet"));

            Assert.IsNotNull(free);
            Assert.IsTrue(free > 0);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ─── Runner ───────────────────────────────────────────────────────────────

    private sealed class Collect : IProgress<BulkUpdateProgress>
    {
        public List<BulkUpdateProgress> Reports { get; } = [];
        public void Report(BulkUpdateProgress value) => Reports.Add(value);
    }

    [TestMethod]
    public async Task Runner_DownloadsInOrder_RecordsFailures_AndCountsBytesAcrossTheBatch()
    {
        var progress = new Collect();
        var fetched = new List<string>();

        var summary = await new BulkDownloadRunner().RunAsync(
            [Mod(1, "A", 100), Mod(2, "B", 300), Mod(3, "C", 50)],
            (item, own, _) =>
            {
                fetched.Add(item.Title);
                own.Report(new UploadProgress("Downloading", (ulong)item.FileSize / 2, (ulong)item.FileSize));
                return Task.FromResult(item.Id != 2);
            },
            progress, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, fetched);
        Assert.AreEqual(2, summary.Downloaded);
        CollectionAssert.AreEqual(new[] { "B" }, summary.Failed.ToArray());
        Assert.IsFalse(summary.Cancelled);
        Assert.IsTrue(progress.Reports.Any(r => r is { Index: 2, Total: 3, BytesDone: 250, BytesTotal: 450 }));
        Assert.AreEqual(450, progress.Reports[^1].BytesDone);
    }

    [TestMethod]
    public async Task Runner_CancelStopsBeforeTheNextMod_AndDuringOne()
    {
        using var cts = new CancellationTokenSource();
        var fetched = 0;

        var summary = await new BulkDownloadRunner().RunAsync(
            [Mod(1, "A"), Mod(2, "B")],
            (_, _, _) =>
            {
                fetched++;
                cts.Cancel();
                return Task.FromResult(true);
            },
            new Collect(), cts.Token);

        Assert.AreEqual(1, fetched);
        Assert.IsTrue(summary.Cancelled);
        Assert.AreEqual(1, summary.Downloaded);

        var during = await new BulkDownloadRunner().RunAsync(
            [Mod(1, "A")],
            (_, _, _) => throw new OperationCanceledException(),
            new Collect(), CancellationToken.None);

        Assert.IsTrue(during.Cancelled);
        Assert.AreEqual(0, during.Downloaded);
    }
}
