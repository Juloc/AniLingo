using Jint;

namespace AniLingo.Tests;

/// <summary>
/// Runs the offline-library download engine (wwwroot/js/offline-library.js)
/// under Jint, the same way DeviceSpeechEngineTests exercises tts.js: the
/// pure decisions (differential manifest diffing, the finalization rule,
/// queue state transitions, backoff and Wi-Fi-only eligibility) are verified
/// exactly as shipped, with no IndexedDB/OPFS/network involved.
/// </summary>
[TestClass]
public sealed class OfflineLibraryEngineTests
{
    private const string Harness = "var window = globalThis;";

    [TestMethod]
    public void DiffManifestOnlyTouchesChangedAndRemovedChapters()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var remote = [
                { chapterId: "a", hash: "h1" },
                { chapterId: "b", hash: "h2-new" },
                { chapterId: "d", hash: "h4" }
            ];
            var stored = [
                { chapterId: "a", hash: "h1" },
                { chapterId: "b", hash: "h2-old" },
                { chapterId: "c", hash: "h3" }
            ];
            var diff = AniLingoOfflineLibrary.diffManifest(remote, stored);
            """);

        Assert.AreEqual("b,d", engine.Evaluate("diff.toFetch.join(',')").AsString(),
            "Only the changed and brand-new chapters are queued.");
        Assert.AreEqual("c", engine.Evaluate("diff.toRemove.join(',')").AsString(),
            "A chapter no longer in the manifest is removed.");
        Assert.AreEqual("a", engine.Evaluate("diff.unchanged.join(',')").AsString(),
            "An identical hash means no transfer.");
    }

    [TestMethod]
    public void DiffManifestHandlesEmptyStoredState()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var diff = AniLingoOfflineLibrary.diffManifest(
                [{ chapterId: "a", hash: "h1" }], []);
            """);

        Assert.AreEqual("a", engine.Evaluate("diff.toFetch.join(',')").AsString());
        Assert.AreEqual(0, Number(engine, "diff.toRemove.length"));
    }

    [TestMethod]
    public void BookIsCompleteOnlyWhenEverySelectedChapterHashMatches()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var refs = [{ chapterId: "a", hash: "h1" }, { chapterId: "b", hash: "h2" }];
            var full = new Map([["a", "h1"], ["b", "h2"]]);
            var partial = new Map([["a", "h1"]]);
            var stale = new Map([["a", "h1"], ["b", "h2-old"]]);
            """);

        Assert.IsTrue(engine.Evaluate("AniLingoOfflineLibrary.isBookComplete(refs, full)").AsBoolean());
        Assert.IsFalse(engine.Evaluate("AniLingoOfflineLibrary.isBookComplete(refs, partial)").AsBoolean(),
            "A chapter that has not been downloaded yet must never count as complete.");
        Assert.IsFalse(engine.Evaluate("AniLingoOfflineLibrary.isBookComplete(refs, stale)").AsBoolean(),
            "A hash mismatch (corrupt/outdated local copy) must never count as complete.");
        Assert.IsFalse(engine.Evaluate("AniLingoOfflineLibrary.isBookComplete([], full)").AsBoolean(),
            "An empty chapter list is never presented as a complete book.");
    }

    [TestMethod]
    public void BackoffIsBoundedAndExponential()
    {
        var engine = CreateEngine();

        Assert.AreEqual(1000, engine.Evaluate("AniLingoOfflineLibrary.computeBackoffMs(0)").AsNumber());
        Assert.AreEqual(2000, engine.Evaluate("AniLingoOfflineLibrary.computeBackoffMs(1)").AsNumber());
        Assert.AreEqual(4000, engine.Evaluate("AniLingoOfflineLibrary.computeBackoffMs(2)").AsNumber());
        Assert.AreEqual(
            30000,
            engine.Evaluate("AniLingoOfflineLibrary.computeBackoffMs(20)").AsNumber(),
            "Backoff must be capped, not grow unbounded with repeated failures.");
    }

    [TestMethod]
    public void QueueItemTransitionsFollowTheStateMachineAndIgnoreInvalidEvents()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var e = AniLingoOfflineLibrary;
            var queued = { state: e.QUEUE_STATES.QUEUED, attempts: 0 };
            var downloading = e.transitionQueueItem(queued, { type: "start" });
            var verified = e.transitionQueueItem(downloading, { type: "success" });
            var failedOnce = e.transitionQueueItem(
                e.transitionQueueItem(queued, { type: "start" }), { type: "failure" });
            var retried = e.transitionQueueItem(failedOnce, { type: "retry" });
            var pausedFromQueued = e.transitionQueueItem(queued, { type: "pause" });
            var resumed = e.transitionQueueItem(pausedFromQueued, { type: "resume" });
            var ignoredResume = e.transitionQueueItem(queued, { type: "resume" });
            var cancelledVerified = e.transitionQueueItem(verified, { type: "cancel" });
            """);

        Assert.AreEqual("downloading", engine.Evaluate("downloading.state").AsString());
        Assert.AreEqual("verified", engine.Evaluate("verified.state").AsString());
        Assert.AreEqual(0, Number(engine, "verified.attempts"));
        Assert.AreEqual("failed", engine.Evaluate("failedOnce.state").AsString());
        Assert.AreEqual(1, Number(engine, "failedOnce.attempts"));
        Assert.AreEqual("queued", engine.Evaluate("retried.state").AsString());
        Assert.AreEqual("paused", engine.Evaluate("pausedFromQueued.state").AsString());
        Assert.AreEqual("queued", engine.Evaluate("resumed.state").AsString());
        Assert.AreEqual(
            "queued",
            engine.Evaluate("ignoredResume.state").AsString(),
            "Resuming an item that is not paused must be a no-op.");
        Assert.AreEqual(
            "verified",
            engine.Evaluate("cancelledVerified.state").AsString(),
            "A verified (completed) item cannot be cancelled away.");
    }

    [TestMethod]
    public void NextEligibleItemSkipsPausedAndBackoffButReportsWhenToRecheck()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var e = AniLingoOfflineLibrary;
            var now = 10000;
            var queue = [
                { id: "1", state: e.QUEUE_STATES.PAUSED },
                { id: "2", state: e.QUEUE_STATES.QUEUED, retryAtMs: now + 5000 },
                { id: "3", state: e.QUEUE_STATES.QUEUED, retryAtMs: 0 }
            ];
            var picked = e.nextEligibleItem(queue, now);
            var onlyBackoff = e.nextEligibleItem(
                [{ id: "2", state: e.QUEUE_STATES.QUEUED, retryAtMs: now + 5000 }], now);
            """);

        Assert.AreEqual("3", engine.Evaluate("picked.item.id").AsString(),
            "The eligible item is picked even though it is not first in the list.");
        Assert.IsTrue(engine.Evaluate("onlyBackoff.item === null").AsBoolean());
        Assert.AreEqual(15000, Number(engine, "onlyBackoff.nextEligibleAtMs"),
            "When everything is in backoff, the caller learns when to look again.");
    }

    [TestMethod]
    public void NetworkEligibilityIsBestEffortAndOnlyEnforcedWhenWifiOnlyIsRequested()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var e = AniLingoOfflineLibrary;
            var wifi = { type: "wifi" };
            var cellular = { type: "cellular" };
            var saveData = { saveData: true };
            var slow = { effectiveType: "2g" };
            var fast = { effectiveType: "4g" };
            """);

        Assert.IsTrue(engine.Evaluate("e.isNetworkEligible(cellular, false)").AsBoolean(),
            "Without Wi-Fi-only, any connection is eligible.");
        Assert.IsTrue(engine.Evaluate("e.isNetworkEligible(wifi, true)").AsBoolean());
        Assert.IsFalse(engine.Evaluate("e.isNetworkEligible(cellular, true)").AsBoolean());
        Assert.IsFalse(engine.Evaluate("e.isNetworkEligible(saveData, true)").AsBoolean());
        Assert.IsFalse(engine.Evaluate("e.isNetworkEligible(slow, true)").AsBoolean());
        Assert.IsTrue(engine.Evaluate("e.isNetworkEligible(fast, true)").AsBoolean());
        Assert.IsTrue(
            engine.Evaluate("e.isNetworkEligible(null, true)").AsBoolean(),
            "The Network Information API is best-effort: unavailable must not silently block downloads forever.");
    }

    [TestMethod]
    public void NamespaceKeyIsolatesAccountsAndFormatBytesIsHumanReadable()
    {
        var engine = CreateEngine();

        Assert.AreEqual(
            "acct-1:manifests",
            engine.Evaluate("AniLingoOfflineLibrary.namespaceKey('acct-1', 'manifests')").AsString());
        Assert.AreNotEqual(
            engine.Evaluate("AniLingoOfflineLibrary.namespaceKey('acct-1', 'manifests')").AsString(),
            engine.Evaluate("AniLingoOfflineLibrary.namespaceKey('acct-2', 'manifests')").AsString());

        Assert.AreEqual("0 B", engine.Evaluate("AniLingoOfflineLibrary.formatBytes(0)").AsString());
        Assert.AreEqual("512 B", engine.Evaluate("AniLingoOfflineLibrary.formatBytes(512)").AsString());
        Assert.AreEqual("1 KB", engine.Evaluate("AniLingoOfflineLibrary.formatBytes(1024)").AsString());
        Assert.AreEqual("2.5 MB", engine.Evaluate("AniLingoOfflineLibrary.formatBytes(2.5 * 1024 * 1024)").AsString());
    }

    [TestMethod]
    public void TotalStorageBytesSumsAcrossBooksAndChapters()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var books = [
                { chapters: [{ sizeBytes: 100 }, { sizeBytes: 200 }] },
                { chapters: [{ sizeBytes: 50 }] },
                { chapters: [] }
            ];
            """);

        Assert.AreEqual(350, engine.Evaluate("AniLingoOfflineLibrary.totalStorageBytes(books)").AsNumber());
        Assert.AreEqual(0, engine.Evaluate("AniLingoOfflineLibrary.totalStorageBytes([])").AsNumber());
    }

    private static Engine CreateEngine()
    {
        var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(10)));
        engine.Execute(Harness);
        engine.Execute(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AniLingo.Web", "wwwroot", "js", "offline-library.js")));
        return engine;
    }

    private static double Number(Engine engine, string expression) =>
        engine.Evaluate(expression).AsNumber();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AniLingo.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AniLingo repository root.");
    }
}
