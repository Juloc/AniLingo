using System.Text.RegularExpressions;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

/// <summary>
/// Issue #153 (novel reader navigation and annotation polish): chapter/volume
/// labels on work notes, renaming a bookmark, editing a highlight's note,
/// bounded/paged/profile-isolated search, and previous/next bookmark
/// navigation across the whole work.
/// </summary>
[TestClass]
public sealed class NovelAnnotationPolishTests
{
    private const string ReaderA = "reader-a";
    private const string ReaderB = "reader-b";

    [TestMethod]
    public async Task WorkNotesCarryChapterAndVolumeLabels()
    {
        using var fixture = await Fixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("labelled", chapterCount: 3, epubVolumes: true);
        var current = work.Chapters[0];
        var other = work.Chapters[2];
        var service = new NovelAnnotationService(fixture.Db);

        await service.AddBookmarkAsync(
            ReaderA, other.Id, 100, "ja", 0, 0, null, null, null, CancellationToken.None);

        var page = await service.GetWorkNotesAsync(
            ReaderA, work.Id, current.Id, NovelNoteKind.Bookmark, 0, CancellationToken.None);

        Assert.AreEqual(1, page.Items.Count);
        var note = page.Items[0];
        Assert.AreEqual(other.Number, note.ChapterNumber);
        Assert.AreEqual(other.Title, note.ChapterTitle);
        Assert.AreEqual(3, note.VolumeNumber);
        Assert.IsNotNull(note.VolumeTitle);
    }

    [TestMethod]
    public async Task WorkNotesOmitVolumeLabelForNonEpubWorks()
    {
        using var fixture = await Fixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("web-novel", chapterCount: 2, epubVolumes: false);
        var service = new NovelAnnotationService(fixture.Db);

        await service.AddBookmarkAsync(
            ReaderA, work.Chapters[1].Id, 50, "ja", 0, 0, null, null, null, CancellationToken.None);

        var page = await service.GetWorkNotesAsync(
            ReaderA, work.Id, work.Chapters[0].Id, NovelNoteKind.Bookmark, 0, CancellationToken.None);

        Assert.AreEqual(1, page.Items.Count);
        Assert.IsNull(page.Items[0].VolumeNumber);
        Assert.IsNull(page.Items[0].VolumeTitle);
    }

    [TestMethod]
    public async Task BookmarkCanBeRenamedAndClearedByOwningProfileOnly()
    {
        using var fixture = await Fixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("rename", chapterCount: 1);
        var service = new NovelAnnotationService(fixture.Db);

        var bookmark = await service.AddBookmarkAsync(
            ReaderA, work.Chapters[0].Id, 0, "ja", 0, 0, null, null, null, CancellationToken.None);
        Assert.IsNull(bookmark.Label);

        var renamed = await service.UpdateBookmarkLabelAsync(
            ReaderA, bookmark.Id, "  Wichtige Szene  ", CancellationToken.None);
        Assert.IsNotNull(renamed);
        Assert.AreEqual("Wichtige Szene", renamed!.Label);

        // Another profile cannot rename someone else's bookmark.
        Assert.IsNull(await service.UpdateBookmarkLabelAsync(
            ReaderB, bookmark.Id, "Hijacked", CancellationToken.None));

        var cleared = await service.UpdateBookmarkLabelAsync(
            ReaderA, bookmark.Id, "   ", CancellationToken.None);
        Assert.IsNull(cleared!.Label);
    }

    [TestMethod]
    public async Task HighlightNoteCanBeEditedAndClearedByOwningProfileOnly()
    {
        using var fixture = await Fixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("edit-note", chapterCount: 1);
        var service = new NovelAnnotationService(fixture.Db);

        var highlight = await service.AddHighlightAsync(
            ReaderA, work.Chapters[0].Id, "ja", 0, 0, 3, null, CancellationToken.None);
        Assert.IsNull(highlight.Note);

        var edited = await service.UpdateHighlightNoteAsync(
            ReaderA, highlight.Id, "Das ist wichtig.", CancellationToken.None);
        Assert.AreEqual("Das ist wichtig.", edited!.Note);

        Assert.IsNull(await service.UpdateHighlightNoteAsync(
            ReaderB, highlight.Id, "Hijacked", CancellationToken.None));

        var cleared = await service.UpdateHighlightNoteAsync(
            ReaderA, highlight.Id, "", CancellationToken.None);
        Assert.IsNull(cleared!.Note);
    }

    [TestMethod]
    public async Task SearchIsBoundedPagedAndProfileIsolated()
    {
        using var fixture = await Fixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("search", chapterCount: 50);
        var service = new NovelAnnotationService(fixture.Db);

        for (var index = 0; index < 45; index++)
        {
            await service.AddBookmarkAsync(
                ReaderA, work.Chapters[index % work.Chapters.Count].Id, index % 1000, "ja", null, 0,
                "Findbar per Suche",
                null, null, CancellationToken.None);
        }

        // Another profile's matching bookmarks must never leak into the results.
        await service.AddBookmarkAsync(
            ReaderB, work.Chapters[0].Id, 0, "ja", null, 0, "Findbar per Suche", null, null,
            CancellationToken.None);

        var first = await service.SearchWorkNotesAsync(
            ReaderA, work.Id, NovelNoteKind.Bookmark, "findbar", 0, CancellationToken.None);
        Assert.AreEqual(NovelAnnotationService.WorkNotesPageSize, first.Items.Count);
        Assert.IsTrue(first.HasMore);
        Assert.IsTrue(first.Items.All(x => x.Title!.Contains("Findbar", StringComparison.Ordinal)));

        var second = await service.SearchWorkNotesAsync(
            ReaderA, work.Id, NovelNoteKind.Bookmark, "findbar", first.NextOffset, CancellationToken.None);
        Assert.IsFalse(second.HasMore);
        Assert.AreEqual(0, first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)).Count());

        var otherProfile = await service.SearchWorkNotesAsync(
            ReaderB, work.Id, NovelNoteKind.Bookmark, "findbar", 0, CancellationToken.None);
        Assert.AreEqual(1, otherProfile.Items.Count);

        var noMatches = await service.SearchWorkNotesAsync(
            ReaderA, work.Id, NovelNoteKind.Bookmark, "nichts-passt", 0, CancellationToken.None);
        Assert.AreEqual(0, noMatches.Items.Count);
    }

    [TestMethod]
    public async Task SearchMatchesHighlightTextOrNoteAndEscapesWildcards()
    {
        using var fixture = await Fixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("search-highlights", chapterCount: 1);
        var service = new NovelAnnotationService(fixture.Db);
        var chapter = work.Chapters[0];

        await service.AddHighlightAsync(ReaderA, chapter.Id, "ja", 0, 0, 3, "Kommentar mit 100%", CancellationToken.None);
        await service.AddHighlightAsync(ReaderA, chapter.Id, "ja", 1, 0, 3, null, CancellationToken.None);

        var byNote = await service.SearchWorkNotesAsync(
            ReaderA, work.Id, NovelNoteKind.Highlight, "Kommentar", 0, CancellationToken.None);
        Assert.AreEqual(1, byNote.Items.Count);

        // "%" in the query is a literal, not a SQL LIKE wildcard.
        var literalPercent = await service.SearchWorkNotesAsync(
            ReaderA, work.Id, NovelNoteKind.Highlight, "100%", 0, CancellationToken.None);
        Assert.AreEqual(1, literalPercent.Items.Count);

        // A bare "%" is a literal character to search for, not a SQL wildcard;
        // it only matches the note that actually contains a percent sign.
        var wildcardOnly = await service.SearchWorkNotesAsync(
            ReaderA, work.Id, NovelNoteKind.Highlight, "%", 0, CancellationToken.None);
        Assert.AreEqual(1, wildcardOnly.Items.Count);

        var noLiteralMatch = await service.SearchWorkNotesAsync(
            ReaderA, work.Id, NovelNoteKind.Highlight, "_", 0, CancellationToken.None);
        Assert.AreEqual(0, noLiteralMatch.Items.Count);
    }

    [TestMethod]
    public async Task AdjacentBookmarkOrdersByChapterThenPositionAndWrapsAround()
    {
        using var fixture = await Fixture.CreateAsync();
        var work = await fixture.SeedWorkAsync("adjacent", chapterCount: 5);
        var service = new NovelAnnotationService(fixture.Db);

        var early = await service.AddBookmarkAsync(
            ReaderA, work.Chapters[0].Id, 200, "ja", null, 0, "early", null, null, CancellationToken.None);
        var middleA = await service.AddBookmarkAsync(
            ReaderA, work.Chapters[2].Id, 100, "ja", null, 0, "middle-a", null, null, CancellationToken.None);
        var middleB = await service.AddBookmarkAsync(
            ReaderA, work.Chapters[2].Id, 800, "ja", null, 0, "middle-b", null, null, CancellationToken.None);
        var late = await service.AddBookmarkAsync(
            ReaderA, work.Chapters[4].Id, 500, "ja", null, 0, "late", null, null, CancellationToken.None);

        // Standing between middleA and middleB in chapter 3 (index 2, number 3).
        var next = await service.GetAdjacentBookmarkAsync(
            ReaderA, work.Id, work.Chapters[2].Number, 300, forward: true, CancellationToken.None);
        Assert.AreEqual(middleB.Id, next!.Id);

        var previous = await service.GetAdjacentBookmarkAsync(
            ReaderA, work.Id, work.Chapters[2].Number, 300, forward: false, CancellationToken.None);
        Assert.AreEqual(middleA.Id, previous!.Id);

        // Past the last bookmark, forward wraps around to the first.
        var wrappedForward = await service.GetAdjacentBookmarkAsync(
            ReaderA, work.Id, work.Chapters[4].Number, 999, forward: true, CancellationToken.None);
        Assert.AreEqual(early.Id, wrappedForward!.Id);

        // Before the first bookmark, backward wraps around to the last.
        var wrappedBackward = await service.GetAdjacentBookmarkAsync(
            ReaderA, work.Id, work.Chapters[0].Number, 0, forward: false, CancellationToken.None);
        Assert.AreEqual(late.Id, wrappedBackward!.Id);

        // A profile with no bookmarks in the work finds nothing.
        Assert.IsNull(await service.GetAdjacentBookmarkAsync(
            ReaderB, work.Id, work.Chapters[0].Number, 0, forward: true, CancellationToken.None));
    }

    [TestMethod]
    public void ReaderShortcutKeysDoNotCollideWithEachOtherOrTheSharedReaderShell()
    {
        var scriptPath = FindRepoFile("src/AniLingo.Web/wwwroot/js/novel-annotations.js");
        var script = File.ReadAllText(scriptPath);

        var match = Regex.Match(
            script,
            @"const READER_SHORTCUTS = \{(?<body>[^}]*)\};",
            RegexOptions.Singleline);
        Assert.IsTrue(match.Success, "Expected a READER_SHORTCUTS constant in novel-annotations.js.");

        var keys = Regex.Matches(match.Groups["body"].Value, @":\s*""(?<key>[^""]+)""")
            .Select(x => x.Groups["key"].Value)
            .ToList();

        Assert.IsTrue(keys.Count >= 4, "Expected at least four shortcut keys (bookmark, notes, previous/next bookmark).");
        CollectionAssert.AllItemsAreUnique(keys, "Shortcut keys must not collide with each other.");

        // Keys already used elsewhere in the novel reader page: reader-shell.js
        // and the chapter/notes drawers use Escape; reader-personalization.js
        // uses ArrowLeft/ArrowRight/PageUp/PageDown in paged mode; reader-tts.js
        // tracks Space/Arrow/PageUp/PageDown as user-scroll signals.
        var reserved = new[] { "Escape", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "PageUp", "PageDown", " " };
        foreach (var key in keys)
        {
            CollectionAssert.DoesNotContain(reserved, key, $"Shortcut key '{key}' collides with an existing reader shortcut.");
        }
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above {AppContext.BaseDirectory}.");
    }

    private sealed record SeededWork(Guid Id, IReadOnlyList<NovelChapter> Chapters);

    private sealed class Fixture : IDisposable
    {
        private readonly string path;

        private Fixture(string path, AppDbContext db)
        {
            this.path = path;
            Db = db;
        }

        public AppDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"anilingo-novel-polish-{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={path};Foreign Keys=True";
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(path, db);
        }

        public async Task<SeededWork> SeedWorkAsync(string key, int chapterCount, bool epubVolumes = false)
        {
            var work = new NovelWork
            {
                SourceProvider = "polish-tests",
                SourceKey = key,
                SourceUrl = $"https://example.invalid/{key}",
                Title = key
            };

            var chapters = new List<NovelChapter>();
            if (epubVolumes)
            {
                for (var volumeNumber = 1; volumeNumber <= chapterCount; volumeNumber++)
                {
                    var volume = new NovelVolume
                    {
                        WorkId = work.Id,
                        Number = volumeNumber,
                        Title = $"Volume {volumeNumber}",
                        Kind = NovelVolumeKinds.Epub,
                        SourceKey = $"{key}-vol-{volumeNumber}"
                    };
                    Db.Add(volume);
                    chapters.Add(new NovelChapter
                    {
                        WorkId = work.Id,
                        VolumeId = volume.Id,
                        Number = volumeNumber,
                        SourceUrl = $"https://example.invalid/{key}/{volumeNumber}",
                        Title = $"Title {volumeNumber}",
                        OriginalText = $"第{volumeNumber}章の最初の段落です。\n\n二番目の段落はここにあります。",
                        SourceHash = $"HASH-{key}-{volumeNumber}"
                    });
                }
            }
            else
            {
                var volume = new NovelVolume { WorkId = work.Id, Number = 1, SourceKey = "web" };
                Db.Add(volume);
                for (var number = 1; number <= chapterCount; number++)
                {
                    chapters.Add(new NovelChapter
                    {
                        WorkId = work.Id,
                        VolumeId = volume.Id,
                        Number = number,
                        SourceUrl = $"https://example.invalid/{key}/{number}",
                        Title = $"Title {number}",
                        OriginalText = $"第{number}章の最初の段落です。\n\n二番目の段落はここにあります。",
                        SourceHash = $"HASH-{key}-{number}"
                    });
                }
            }

            Db.Add(work);
            Db.AddRange(chapters);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return new SeededWork(work.Id, chapters);
        }

        public void Dispose()
        {
            Db.Dispose();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
