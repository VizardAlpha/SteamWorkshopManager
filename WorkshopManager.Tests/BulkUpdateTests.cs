using System.Text;
using Steamworks;
using WorkshopManager.Core.Workshop;
using WorkshopManager.Models;
using WorkshopManager.Services.Steam;
using WorkshopManager.Services.Workshop;

namespace WorkshopManager.Tests;

[TestClass]
public class BulkUpdateTests
{
    private static WorkshopItem Mod(ulong id, string title) => new() { Id = id, Title = title };

    private static BulkUpdateEntry Entry(ulong id, string title, long size = 100) =>
        new(Mod(id, title), "folder", BulkUpdateState.Ready, size);

    private static string NewFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "swm-bulk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ─── Change notes ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Changelog_OneTextForAll()
    {
        var notes = BulkChangelog.ForAll("  Fixed things  ");

        Assert.AreEqual("Fixed things", notes.For(Mod(1, "A")));
        Assert.AreEqual(0, notes.SectionCount);
    }

    [TestMethod]
    public void Changelog_SectionsByTitleOrId_OthersGetTheLeadingText()
    {
        var notes = BulkChangelog.Parse("General fix\n\n# Alpha Mod\nAlpha notes\n## Details\nstay\n\n# 42\nBy id\n", markdown: false);

        Assert.AreEqual("Alpha notes\n## Details\nstay", notes.For(Mod(1, "alpha mod")));
        Assert.AreEqual("By id", notes.For(Mod(42, "Whatever")));
        Assert.AreEqual("General fix", notes.For(Mod(3, "Other")));
        Assert.IsTrue(notes.HasSectionFor(Mod(42, "Whatever")));
        Assert.IsFalse(notes.HasSectionFor(Mod(3, "Other")));
    }

    [TestMethod]
    public void Changelog_FileWithoutHeading_AppliesToEveryMod()
    {
        var notes = BulkChangelog.Parse("Line one\nLine two", markdown: false);

        Assert.AreEqual("Line one\nLine two", notes.For(Mod(1, "A")));
    }

    [TestMethod]
    public void Changelog_MarkdownBodiesAreConvertedToBbCode()
    {
        var notes = BulkChangelog.Parse("# A\n**bold** fix", markdown: true);

        StringAssert.Contains(notes.For(Mod(1, "A")), "[b]bold[/b]");
    }

    [TestMethod]
    public void Changelog_ReportsHeadingsThatMatchNoMod_AndTooLongNotes()
    {
        var tooLong = new string((char)120, BulkChangelog.MaxNoteLength + 1);
        var notes = BulkChangelog.Parse("# Alpha\nok\n# Typo Mod\n" + tooLong, markdown: false);
        var mods = new[] { Mod(1, "Alpha"), Mod(2, "Beta") };

        CollectionAssert.AreEqual(new[] { "Typo Mod" }, notes.SectionsWithoutMod(mods).ToArray());
        Assert.AreEqual(0, notes.TooLongFor(mods).Count);
        Assert.AreEqual(1, notes.TooLongFor([Mod(9, "Typo Mod")]).Count);
    }

    [TestMethod]
    public void Changelog_TypedTextReplacesTheLeadingTextOfTheFile()
    {
        var notes = BulkChangelog.Parse("from file\n# A\nown", markdown: false).WithFallback("typed");

        Assert.AreEqual("own", notes.For(Mod(1, "A")));
        Assert.AreEqual("typed", notes.For(Mod(2, "B")));
    }

    // ─── Change notes file ────────────────────────────────────────────────────

    [TestMethod]
    public void ChangelogFile_AcceptsTxtAndMd_RefusesOtherTypesBinaryAndEmpty()
    {
        var dir = NewFolder();
        try
        {
            var txt = Path.Combine(dir, "notes.txt");
            File.WriteAllText(txt, "# A\nnotes", new UTF8Encoding(true));
            var md = Path.Combine(dir, "notes.MD");
            File.WriteAllText(md, "# A\n*notes*");
            var docx = Path.Combine(dir, "notes.docx");
            File.WriteAllText(docx, "text");
            var binary = Path.Combine(dir, "binary.txt");
            File.WriteAllBytes(binary, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00]);
            var empty = Path.Combine(dir, "empty.md");
            File.WriteAllText(empty, "  \n");

            Assert.AreEqual("notes", BulkChangelogFile.Read(txt).Changelog!.For(Mod(1, "A")));
            Assert.IsTrue(BulkChangelogFile.Read(md).IsValid);
            Assert.AreEqual("BulkChangelogWrongType", BulkChangelogFile.Read(docx).ErrorKey);
            Assert.AreEqual("BulkChangelogNotText", BulkChangelogFile.Read(binary).ErrorKey);
            Assert.AreEqual("BulkChangelogEmpty", BulkChangelogFile.Read(empty).ErrorKey);
            Assert.AreEqual("BulkChangelogUnreadable", BulkChangelogFile.Read(Path.Combine(dir, "missing.txt")).ErrorKey);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ─── Plan ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Plan_ClassifiesNoFolderMissingUnchangedAndChanged()
    {
        var dir = NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "mod.pak"), "content");
            var recorded = ModFileInfoBuilder.BuildForFolder(dir)!;
            var mod = Mod(1, "A");

            Assert.AreEqual(BulkUpdateState.NoFolder, BulkUpdatePlanner.Classify(mod, null).State);
            Assert.AreEqual(BulkUpdateState.FolderMissing,
                BulkUpdatePlanner.Classify(mod, new ItemFileInfo { Path = Path.Combine(dir, "gone") }).State);
            Assert.AreEqual(BulkUpdateState.Unchanged, BulkUpdatePlanner.Classify(mod, recorded).State);
            // Picked in the editor but never uploaded: no recorded size.
            Assert.AreEqual(BulkUpdateState.Ready, BulkUpdatePlanner.Classify(mod, new ItemFileInfo { Path = dir }).State);

            File.WriteAllText(Path.Combine(dir, "mod.pak"), "content changed");
            var changed = BulkUpdatePlanner.Classify(mod, recorded);
            Assert.AreEqual(BulkUpdateState.Ready, changed.State);
            Assert.AreEqual("content changed".Length, changed.SizeBytes);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ─── Runner ───────────────────────────────────────────────────────────────

    private static BulkUpdateRunner InstantRunner() =>
        new((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

    private sealed class Collect : IProgress<BulkUpdateProgress>
    {
        public List<BulkUpdateProgress> Reports { get; } = [];
        public void Report(BulkUpdateProgress value) => Reports.Add(value);
    }

    [TestMethod]
    public async Task Runner_SendsEveryModInOrder_AndCountsBytesAcrossTheBatch()
    {
        var sent = new List<string>();
        var progress = new Collect();

        var summary = await InstantRunner().RunAsync(
            [Entry(1, "A", 100), Entry(2, "B", 300)],
            (entry, own) =>
            {
                sent.Add(entry.Item.Title);
                own.Report(new UploadProgress("Uploading", (ulong)entry.SizeBytes / 2, (ulong)entry.SizeBytes));
                return Task.FromResult(new UpdateItemOutcome(true));
            },
            progress, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "A", "B" }, sent);
        Assert.AreEqual(2, summary.Updated);
        Assert.AreEqual(0, summary.Failed.Count);
        Assert.IsFalse(summary.Cancelled);
        Assert.IsTrue(progress.Reports.Any(r => r is { Index: 2, Total: 2, BytesDone: 250, BytesTotal: 400 }));
        Assert.AreEqual(400, progress.Reports[^1].BytesDone);
    }

    [TestMethod]
    public async Task Runner_AHardFailureIsRecorded_AndTheBatchGoesOn()
    {
        var summary = await InstantRunner().RunAsync(
            [Entry(1, "A"), Entry(2, "B")],
            (entry, _) => Task.FromResult(entry.Item.Id == 1
                ? new UpdateItemOutcome(false, Result: EResult.k_EResultFileNotFound)
                : new UpdateItemOutcome(true, NeedsLegalAgreement: true)),
            new Collect(), CancellationToken.None);

        Assert.AreEqual(1, summary.Updated);
        Assert.AreEqual(new BulkUpdateFailure("A", EResult.k_EResultFileNotFound), summary.Failed.Single());
        Assert.IsTrue(summary.LegalAgreementPending);
    }

    [TestMethod]
    public async Task Runner_WhenSteamPushesBack_WaitsThenRetriesTheSameMod()
    {
        var attempts = 0;
        var progress = new Collect();

        var summary = await InstantRunner().RunAsync(
            [Entry(1, "A")],
            (_, _) => Task.FromResult(++attempts < 3
                ? new UpdateItemOutcome(false, Result: EResult.k_EResultRateLimitExceeded)
                : new UpdateItemOutcome(true)),
            progress, CancellationToken.None);

        Assert.AreEqual(3, attempts);
        Assert.AreEqual(1, summary.Updated);
        Assert.IsTrue(progress.Reports.Any(r => r.RetryIn == BulkUpdateRunner.RateLimitWaits[0]));
        Assert.IsTrue(progress.Reports.Any(r => r.RetryIn == BulkUpdateRunner.RateLimitWaits[1]));
    }

    [TestMethod]
    public async Task Runner_AModSteamKeepsRefusingIsGivenUpOn_AndTheNextOneIsStillSent()
    {
        var attemptsOnA = 0;
        var progress = new Collect();

        var summary = await InstantRunner().RunAsync(
            [Entry(1, "A"), Entry(2, "B")],
            (entry, _) =>
            {
                if (entry.Item.Id != 1) return Task.FromResult(new UpdateItemOutcome(true));
                attemptsOnA++;
                return Task.FromResult(new UpdateItemOutcome(false, Result: EResult.k_EResultRateLimitExceeded));
            },
            progress, CancellationToken.None);

        Assert.AreEqual(1, summary.Updated);
        Assert.AreEqual(new BulkUpdateFailure("A", EResult.k_EResultRateLimitExceeded), summary.Failed.Single());
        Assert.IsFalse(summary.Cancelled);

        // Frozen on A for the growing waits, then the longest one again, never past the ceiling.
        var waits = progress.Reports.Where(r => r is { Index: 1, RetryIn: not null })
            .Select(r => r.RetryIn!.Value).Where(BulkUpdateRunner.RateLimitWaits.Contains).ToList();
        Assert.IsTrue(attemptsOnA > BulkUpdateRunner.RateLimitWaits.Count + 1);
        Assert.IsTrue(waits.Contains(BulkUpdateRunner.RateLimitWaits[^1]));
    }

    [TestMethod]
    public async Task Runner_ABriefOutageIsRetriedAFewTimesOnly()
    {
        var attempts = 0;

        var summary = await InstantRunner().RunAsync(
            [Entry(1, "A")],
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(new UpdateItemOutcome(false, Result: EResult.k_EResultServiceUnavailable));
            },
            new Collect(), CancellationToken.None);

        Assert.AreEqual(BulkUpdateRunner.OutageWaits.Count + 1, attempts);
        Assert.AreEqual(1, summary.Failed.Count);
    }

    [TestMethod]
    public async Task Runner_CancelStopsBeforeTheNextMod()
    {
        using var cts = new CancellationTokenSource();
        var sent = 0;

        var summary = await InstantRunner().RunAsync(
            [Entry(1, "A"), Entry(2, "B")],
            (_, _) =>
            {
                sent++;
                cts.Cancel();
                return Task.FromResult(new UpdateItemOutcome(true));
            },
            new Collect(), cts.Token);

        Assert.AreEqual(1, sent);
        Assert.IsTrue(summary.Cancelled);
        Assert.AreEqual(1, summary.Updated);
    }

    [TestMethod]
    [DataRow(EResult.k_EResultRateLimitExceeded, true, false)]
    [DataRow(EResult.k_EResultTooManyPending, true, false)]
    [DataRow(EResult.k_EResultBusy, false, true)]
    [DataRow(EResult.k_EResultTimeout, false, true)]
    [DataRow(EResult.k_EResultFileNotFound, false, false)]
    [DataRow(EResult.k_EResultLimitExceeded, false, false)]
    [DataRow(EResult.k_EResultAccessDenied, false, false)]
    public void Runner_TellsSlowDownFromOutageFromHardFailure(EResult result, bool rateLimit, bool outage)
    {
        Assert.AreEqual(rateLimit, BulkUpdateRunner.IsRateLimit(result));
        Assert.AreEqual(outage, BulkUpdateRunner.IsOutage(result));
    }
}
