using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Novels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class BookReaderAnnotationsTests
{
    [TestMethod]
    public async Task ChapterNavigationIsLazySearchableAndOrdered()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var (work, first, second) = await SeedBookAsync(db);

            var all = await BookReaderAnnotationStore.GetChaptersAsync(
                db,
                work.Id,
                null,
                CancellationToken.None);

            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(first.Id, all[0].Id);
            Assert.AreEqual(second.Id, all[1].Id);

            var filtered = await BookReaderAnnotationStore.GetChaptersAsync(
                db,
                work.Id,
                "Two",
                CancellationToken.None);

            Assert.AreEqual(1, filtered.Count);
            Assert.AreEqual(second.Id, filtered[0].Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task HighlightPersistsAndRejectsOverlap()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var (work, first, _) = await SeedBookAsync(db);

            var reader = new BookReaderChapter(
                work,
                first,
                null,
                ["Hello world.", "Second paragraph."],
                [],
                null,
                null,
                null,
                [],
                "en",
                "id");

            var created = await BookReaderAnnotationStore.AddHighlightAsync(
                db,
                "profile-a",
                reader,
                "original",
                0,
                0,
                5,
                "Greeting",
                CancellationToken.None);

            Assert.AreEqual("Hello", created.Text);
            Assert.AreEqual("Greeting", created.Note);
            Assert.AreEqual("original", created.Language);

            var stored = await BookReaderAnnotationStore.GetChapterHighlightsAsync(
                db,
                "profile-a",
                first.Id,
                CancellationToken.None);

            Assert.AreEqual(1, stored.Count);
            Assert.AreEqual(created.Id, stored[0].Id);

            await AssertThrowsAsync<InvalidOperationException>(async () =>
                await BookReaderAnnotationStore.AddHighlightAsync(
                    db,
                    "profile-a",
                    reader,
                    "original",
                    0,
                    3,
                    8,
                    null,
                    CancellationToken.None));

            var otherProfile = await BookReaderAnnotationStore.GetChapterHighlightsAsync(
                db,
                "profile-b",
                first.Id,
                CancellationToken.None);

            Assert.AreEqual(0, otherProfile.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task WorkAnnotationsIncludeChapterContextAndDeletesAreProfileIsolated()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var (work, first, second) = await SeedBookAsync(db);

            var bookmark = new NovelBookmark
            {
                ProfileId = "profile-a",
                WorkId = work.Id,
                ChapterId = second.Id,
                PositionPermille = 640,
                Language = "id",
                Label = "Important",
                CreatedAt = DateTime.UtcNow
            };
            db.NovelBookmarks.Add(bookmark);

            var highlight = new NovelHighlight
            {
                ProfileId = "profile-a",
                WorkId = work.Id,
                ChapterId = first.Id,
                Language = "original",
                ParagraphIndex = 0,
                StartOffset = 0,
                EndOffset = 5,
                Text = "Hello",
                CreatedAt = DateTime.UtcNow
            };
            db.NovelHighlights.Add(highlight);
            await db.SaveChangesAsync();

            var annotations = await BookReaderAnnotationStore.GetAnnotationsAsync(
                db,
                "profile-a",
                work.Id,
                CancellationToken.None);

            Assert.AreEqual(1, annotations.Bookmarks.Count);
            Assert.AreEqual(2, annotations.Bookmarks[0].ChapterNumber);
            Assert.AreEqual("Chapter Two", annotations.Bookmarks[0].ChapterTitle);

            Assert.AreEqual(1, annotations.Highlights.Count);
            Assert.AreEqual(1, annotations.Highlights[0].ChapterNumber);
            Assert.AreEqual("Chapter One", annotations.Highlights[0].ChapterTitle);

            var wrongProfileBookmarkDelete =
                await BookReaderAnnotationStore.RemoveBookmarkAsync(
                    db,
                    "profile-b",
                    bookmark.Id,
                    CancellationToken.None);
            var wrongProfileHighlightDelete =
                await BookReaderAnnotationStore.RemoveHighlightAsync(
                    db,
                    "profile-b",
                    highlight.Id,
                    CancellationToken.None);

            Assert.IsFalse(wrongProfileBookmarkDelete);
            Assert.IsFalse(wrongProfileHighlightDelete);
            Assert.AreEqual(1, await db.NovelBookmarks.CountAsync());
            Assert.AreEqual(1, await db.NovelHighlights.CountAsync());

            Assert.IsTrue(
                await BookReaderAnnotationStore.RemoveBookmarkAsync(
                    db,
                    "profile-a",
                    bookmark.Id,
                    CancellationToken.None));
            Assert.IsTrue(
                await BookReaderAnnotationStore.RemoveHighlightAsync(
                    db,
                    "profile-a",
                    highlight.Id,
                    CancellationToken.None));

            Assert.AreEqual(0, await db.NovelBookmarks.CountAsync());
            Assert.AreEqual(0, await db.NovelHighlights.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static async Task<(NovelWork Work, NovelChapter First, NovelChapter Second)>
        SeedBookAsync(AppDbContext db)
    {
        var work = new NovelWork
        {
            SourceProvider = BookCatalogService.ImportedBookProvider,
            SourceKey = "reader-test",
            SourceUrl = "upload://reader-test.epub",
            Title = "Reader Test",
            Format = "EPUB:en"
        };

        var volume = new NovelVolume
        {
            WorkId = work.Id,
            Number = 1,
            Kind = NovelVolumeKinds.Book,
            SourceKey = "book"
        };

        var first = new NovelChapter
        {
            WorkId = work.Id,
            VolumeId = volume.Id,
            Number = 1,
            Title = "Chapter One",
            SourceUrl = "book://reader-test/1",
            OriginalText = "Hello world.\n\nSecond paragraph.",
            SourceHash = "hash-1"
        };

        var second = new NovelChapter
        {
            WorkId = work.Id,
            VolumeId = volume.Id,
            Number = 2,
            Title = "Chapter Two",
            SourceUrl = "book://reader-test/2",
            OriginalText = "Another chapter.",
            SourceHash = "hash-2"
        };

        db.NovelWorks.Add(work);
        db.NovelVolumes.Add(volume);
        db.NovelChapters.AddRange(first, second);
        await db.SaveChangesAsync();

        return (work, first, second);
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-book-reader-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }
}
