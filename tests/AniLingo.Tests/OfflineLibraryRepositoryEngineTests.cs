using Jint;

namespace AniLingo.Tests;

/// <summary>
/// Runs the reader ↔ offline library bridge
/// (wwwroot/js/offline-library-repository.js, #221 part 2A) under Jint, the
/// same way OfflineLibraryEngineTests exercises offline-library.js: the pure
/// decisions (chapter source resolution, sync event building, pending-write
/// reconciliation, offline chapter markup rendering) are verified exactly as
/// shipped, with no IndexedDB/OPFS/DOM/network involved.
/// </summary>
[TestClass]
public sealed class OfflineLibraryRepositoryEngineTests
{
    private const string Harness = "var window = globalThis;";

    [TestMethod]
    public void ResolveChapterSourcePrefersVerifiedLocalRegardlessOfConnectivity()
    {
        var engine = CreateEngine();
        var repository = "AniLingoOfflineLibraryRepository";

        Assert.AreEqual(
            "local",
            engine.Evaluate($"{repository}.resolveChapterSource({{ hasVerifiedLocal: true, isOnline: true }})").AsString(),
            "A verified local copy is used even while online (it is guaranteed identical to the server's current content).");
        Assert.AreEqual(
            "local",
            engine.Evaluate($"{repository}.resolveChapterSource({{ hasVerifiedLocal: true, isOnline: false }})").AsString());
        Assert.AreEqual(
            "server",
            engine.Evaluate($"{repository}.resolveChapterSource({{ hasVerifiedLocal: false, isOnline: true }})").AsString());
        Assert.AreEqual(
            "offline-missing",
            engine.Evaluate($"{repository}.resolveChapterSource({{ hasVerifiedLocal: false, isOnline: false }})").AsString(),
            "Not downloaded and not reachable must be a distinct, explicit state, not silently treated as server/local.");
    }

    [TestMethod]
    public void BuildProgressEventMatchesTheClientOfflineProgressEventWireShape()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var event = AniLingoOfflineLibraryRepository.buildProgressEvent({
                workId: "work-1",
                chapterId: "chapter-1",
                positionPermille: 1500,
                anchorLanguage: "ja",
                anchorParagraphIndex: 4,
                anchorOffset: -3,
                clientEventId: "evt-1",
                nowMs: 1732000000000
            });
            """);

        Assert.AreEqual("evt-1", engine.Evaluate("event.clientEventId").AsString());
        Assert.AreEqual("work-1", engine.Evaluate("event.workId").AsString());
        Assert.AreEqual("chapter-1", engine.Evaluate("event.chapterId").AsString());
        Assert.AreEqual(1000, Number(engine, "event.positionPermille"),
            "Position is clamped to the [0, 1000] permille range even when given an out-of-range value.");
        Assert.AreEqual("ja", engine.Evaluate("event.anchorLanguage").AsString());
        Assert.AreEqual(4, Number(engine, "event.anchorParagraphIndex"));
        Assert.AreEqual(0, Number(engine, "event.anchorOffset"),
            "A negative offset is clamped to zero rather than sent as-is.");
        StringAssert.StartsWith(engine.Evaluate("event.clientTimestampUtc").AsString(), "2024-11-19");
    }

    [TestMethod]
    public void BuildProgressEventTreatsMissingParagraphIndexAsNull()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var event = AniLingoOfflineLibraryRepository.buildProgressEvent({
                workId: "w", chapterId: "c", positionPermille: 10,
                anchorLanguage: null, anchorParagraphIndex: null, anchorOffset: 0,
                clientEventId: "evt", nowMs: 0
            });
            """);

        Assert.IsTrue(engine.Evaluate("event.anchorParagraphIndex === null").AsBoolean());
        Assert.IsTrue(engine.Evaluate("event.anchorLanguage === null").AsBoolean());
    }

    [TestMethod]
    public void BuildBookmarkEventDefaultsToUpsertAndClampsPosition()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var upsert = AniLingoOfflineLibraryRepository.buildBookmarkEvent({
                type: "upsert", workId: "w", chapterId: "c", bookmarkId: "b1",
                language: "de", positionPermille: 5000, paragraphIndex: 2, characterOffset: 10,
                anchorText: "Hallo", label: "Chapter start", style: "ribbon", color: "#fff",
                clientEventId: "evt-2", nowMs: 0
            });
            var remove = AniLingoOfflineLibraryRepository.buildBookmarkEvent({
                type: "remove", workId: "w", chapterId: "c", bookmarkId: "b1",
                clientEventId: "evt-3", nowMs: 0
            });
            var unknownType = AniLingoOfflineLibraryRepository.buildBookmarkEvent({
                type: "something-else", workId: "w", chapterId: "c", bookmarkId: "b2",
                clientEventId: "evt-4", nowMs: 0
            });
            """);

        Assert.AreEqual("upsert", engine.Evaluate("upsert.type").AsString());
        Assert.AreEqual(1000, Number(engine, "upsert.positionPermille"));
        Assert.AreEqual("de", engine.Evaluate("upsert.language").AsString());
        Assert.AreEqual("Chapter start", engine.Evaluate("upsert.label").AsString());
        Assert.AreEqual("remove", engine.Evaluate("remove.type").AsString());
        Assert.AreEqual(
            "upsert",
            engine.Evaluate("unknownType.type").AsString(),
            "Anything other than an explicit 'remove' is treated as an upsert.");
    }

    [TestMethod]
    public void MergePendingBookmarksAppliesEventsOldestFirstAndRemoveWinsOverAnEarlierUpsert()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var server = [{ id: "a", label: "from server" }];
            var pending = [
                { bookmarkId: "b", type: "upsert", chapterId: "c", positionPermille: 100,
                  language: "ja", label: "new bookmark", clientTimestampUtc: "2024-01-01T00:00:01.000Z" },
                { bookmarkId: "a", type: "upsert", chapterId: "c", positionPermille: 50,
                  language: "ja", label: "renamed", clientTimestampUtc: "2024-01-01T00:00:02.000Z" },
                { bookmarkId: "b", type: "remove", clientTimestampUtc: "2024-01-01T00:00:03.000Z" }
            ];
            var merged = AniLingoOfflineLibraryRepository.mergePendingBookmarks(server, pending);
            var byId = {};
            merged.forEach(function (bookmark) { byId[bookmark.id] = bookmark; });
            """);

        Assert.AreEqual(1, (int)Number(engine, "merged.length"),
            "Bookmark b was added then removed (in that order); only the renamed server bookmark remains.");
        Assert.AreEqual("renamed", engine.Evaluate("byId['a'].label").AsString());
    }

    [TestMethod]
    public void MergePendingBookmarksAppliesEventsRegardlessOfInputOrder()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var pending = [
                { bookmarkId: "a", type: "upsert", label: "second", clientTimestampUtc: "2024-01-01T00:00:02.000Z" },
                { bookmarkId: "a", type: "upsert", label: "first", clientTimestampUtc: "2024-01-01T00:00:01.000Z" }
            ];
            var merged = AniLingoOfflineLibraryRepository.mergePendingBookmarks([], pending);
            """);

        Assert.AreEqual(
            "second",
            engine.Evaluate("merged[0].label").AsString(),
            "Events are re-sorted by their own timestamp; the later one (by time, not array position) wins.");
    }

    [TestMethod]
    public void CreateAnchorTextNormalizesWhitespaceAndTruncatesAt180Characters()
    {
        var engine = CreateEngine();

        Assert.IsTrue(engine.Evaluate("AniLingoOfflineLibraryRepository.createAnchorText('  ') === null").AsBoolean());
        Assert.AreEqual(
            "Hello world",
            engine.Evaluate("AniLingoOfflineLibraryRepository.createAnchorText('  Hello\\n\\tworld  ')").AsString());

        engine.Execute("var long = 'a'.repeat(200); var anchor = AniLingoOfflineLibraryRepository.createAnchorText(long);");
        Assert.AreEqual(180, (int)Number(engine, "anchor.length"));
    }

    [TestMethod]
    public void SplitParagraphsMirrorsTheServersBlankLineSeparatedRule()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var paragraphs = AniLingoOfflineLibraryRepository.splitParagraphs(
                "  First paragraph.  \r\n\r\nSecond paragraph.\n\n\n\nThird.  \n\n   \n\n");
            """);

        Assert.AreEqual(3, (int)Number(engine, "paragraphs.length"));
        Assert.AreEqual("First paragraph.", engine.Evaluate("paragraphs[0]").AsString());
        Assert.AreEqual("Second paragraph.", engine.Evaluate("paragraphs[1]").AsString());
        Assert.AreEqual("Third.", engine.Evaluate("paragraphs[2]").AsString());
        Assert.AreEqual(0, (int)Number(engine, "AniLingoOfflineLibraryRepository.splitParagraphs('   ').length"));
    }

    [TestMethod]
    public void RenderBookParagraphsHtmlEscapesAndIndexesParagraphs()
    {
        var engine = CreateEngine();
        var html = engine.Evaluate(
            "AniLingoOfflineLibraryRepository.renderBookParagraphsHtml(['Hello <b>world</b>', 'Second'])").AsString();

        Assert.AreEqual(
            "<p data-book-paragraph=\"0\">Hello &lt;b&gt;world&lt;/b&gt;</p><p data-book-paragraph=\"1\">Second</p>",
            html);
    }

    [TestMethod]
    public void RenderNovelBlocksHtmlMirrorsTheServerMarkupForParagraphsHeadingsAndImages()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var blocks = [
                { kind: "h", level: 1, runs: [{ text: "見出し", ruby: null, emphasis: false, strong: false }] },
                { kind: "img", imageAssetUrl: "/assets/a.jpg", imageAlt: "An illustration" },
                { kind: "p", level: 0, runs: [
                    { text: "強調", ruby: null, emphasis: false, strong: true },
                    { text: "猫", ruby: "ねこ", emphasis: false, strong: false }
                ] }
            ];
            var html = AniLingoOfflineLibraryRepository.renderNovelBlocksHtml(blocks, { germanParagraphs: ["Übersetzung"] });
            """);

        var html = engine.Evaluate("html").AsString();
        StringAssert.Contains(html, "data-reader-segment=\"0\"");
        StringAssert.Contains(html, "data-index=\"0\"");
        StringAssert.Contains(html, "role=\"heading\"");
        StringAssert.Contains(html, "aria-level=\"2\"");
        StringAssert.Contains(html, "<figure class=\"novel-reader-illustration\">");
        StringAssert.Contains(html, "src=\"/assets/a.jpg\"");
        StringAssert.Contains(html, "<strong>強調</strong>");
        StringAssert.Contains(html, "<ruby>猫<rt data-rt=\"ねこ\"></rt></ruby>");
        StringAssert.Contains(html, "data-reader-segment=\"1\"");
        StringAssert.Contains(
            html,
            "<p class=\"novel-reader-paragraph de is-heading\" lang=\"de\" role=\"heading\" aria-level=\"2\" data-reader-paragraph data-language=\"de\" data-index=\"0\">Übersetzung</p>",
            "The German paragraph is appended to the section of the text block sharing its paragraph index (0, the heading) — index-aligned exactly like the server.");
        Assert.IsFalse(
            html.Contains("data-language=\"de\" data-index=\"1\""),
            "No German paragraph exists at index 1 (only one was supplied), so the second text block gets no German sibling.");
    }

    [TestMethod]
    public void RenderNovelBlocksHtmlSkipsImagesWithoutAnAssetUrl()
    {
        var engine = CreateEngine();
        var html = engine.Evaluate(
            "AniLingoOfflineLibraryRepository.renderNovelBlocksHtml([{ kind: 'img', imageAssetUrl: null }], {})").AsString();

        Assert.AreEqual("", html);
    }

    private static Engine CreateEngine()
    {
        var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(10)));
        engine.Execute(Harness);
        engine.Execute(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AniLingo.Web", "wwwroot", "js", "offline-library-repository.js")));
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
